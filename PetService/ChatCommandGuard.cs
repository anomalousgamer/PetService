using Dalamud.Hooking;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.Shell;
using PetService.Core;

namespace PetService;

internal sealed unsafe class ChatCommandGuard : IDisposable
{
    private readonly Func<bool> reminderActive;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private Hook<ShellCommandModule.Delegates.ExecuteCommandInner>? hook;
    private long lastNotice;
    internal Func<bool>? Sleeping {get;set;}
    internal Func<DynamicSettings?>? SpeechSettings {get;set;}
    private bool transforming;
    public bool Available => hook is { IsEnabled: true, IsDisposed: false };

    public ChatCommandGuard(IGameInteropProvider interop, Func<bool> reminderActive, IChatGui chat, IPluginLog log)
    {
        this.reminderActive = reminderActive;
        this.chat = chat;
        this.log = log;
        try
        {
            hook = interop.HookFromAddress<ShellCommandModule.Delegates.ExecuteCommandInner>(
                (nint)ShellCommandModule.MemberFunctionPointers.ExecuteCommandInner, Execute);
            hook.Enable();
        }
        catch (Exception exception)
        {
            hook?.Dispose();
            hook = null;
            log.Error(exception, "Could not install chat command guard; gameplay guarding is unavailable.");
        }
    }

    private void Execute(ShellCommandModule* module, Utf8String* command, UIModule* uiModule)
    {
        bool allowed;
        try
        {
            allowed = Sleeping?.Invoke()!=true && (!reminderActive()
                || (command != null && ChatCommandPolicy.Allows(command->ToString())));
        }
        catch (Exception exception)
        {
            log.Error(exception, "Could not classify a chat command; command blocked.");
            return;
        }

        if (allowed)
        {
            // Keep the original outside our catch; never execute a command twice.
            // No text, payload, routing or recipient is logged or persisted.
            if(!transforming && command!=null && SpeechSettings?.Invoke() is {GarbleEnabled:true} options) {
                try {
                    var parsed=SeString.Parse(command->StringPtr);
                    var first=parsed.Payloads.FirstOrDefault() as TextPayload;
                    if(first is not null) {
                        var start=SpeechGarbler.BodyStart(first.Text??"");
                        if(start>=0) {
                            var builder=new SeStringBuilder();var inLink=false;var firstText=true;
                            foreach(var payload in parsed.Payloads) {
                                // Preserve complete structured links, including their visible labels.
                                if(payload is ItemPayload or MapLinkPayload or PlayerPayload)inLink=true;
                                if(payload is TextPayload tp && !inLink) {
                                    var text=tp.Text??"";var prefix=firstText?Math.Min(start,text.Length):0;
                                    builder.AddText(text[..prefix]+SpeechGarbler.Body(text[prefix..],options.GarbleStrength,options.GarbleStyle));
                                } else builder.Add(payload);
                                if(payload is RawPayload raw && raw.Data.AsSpan().SequenceEqual(RawPayload.LinkTerminator.Data))inLink=false;
                                if(payload is TextPayload)firstText=false;
                            }
                            var bytes=builder.Build().Encode();
                            if(bytes.Length<=500)command->SetString(bytes);
                        }
                    }
                } catch { /* Preserve the original when an unfamiliar payload cannot be handled. */ }
            }
            transforming=true;
            try {hook!.Original(module, command, uiModule);}finally{transforming=false;}
            return;
        }
        if (Environment.TickCount64 - lastNotice < 1500)
            return;
        lastNotice = Environment.TickCount64;
        try
        {
            chat.PrintError("[Pet Service] Gameplay is bonded. Chat remains available outside Sleep.");
        }
        catch (Exception exception)
        {
            log.Error(exception, "Could not display the blocked-command notice.");
        }
    }

    public bool StopAutorun()
    {
        try
        {
            var shell = RaptureShellModule.Instance();
            var uiModule = UIModule.Instance();
            if (shell == null || uiModule == null)
                return false;
            using var command = new Utf8String("/automove off");
            // The sole internal exception is this fixed stop command. User chat
            // still passes through Execute and cannot invoke gameplay commands.
            if (Available)
                hook!.Original((ShellCommandModule*)shell, &command, uiModule);
            else
                shell->ExecuteCommandInner(&command, uiModule);
            return true;
        }
        catch (Exception exception)
        {
            log.Error(exception, "Could not cancel autorun when the reminder appeared.");
            return false;
        }
    }

    public void Dispose() => hook?.Dispose();
}
