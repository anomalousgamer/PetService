using Dalamud.Hooking;
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
            log.Error(exception, "Could not install chat command guard; chat stays blocked during reminders.");
        }
    }

    private void Execute(ShellCommandModule* module, Utf8String* command, UIModule* uiModule)
    {
        bool allowed;
        try
        {
            allowed = !reminderActive()
                || (command != null && ChatCommandPolicy.Allows(command->ToString()));
        }
        catch (Exception exception)
        {
            log.Error(exception, "Could not classify a chat command; command blocked.");
            return;
        }

        if (allowed)
        {
            // Keep the original outside our catch; never execute a command twice.
            hook!.Original(module, command, uiModule);
            return;
        }
        if (Environment.TickCount64 - lastNotice < 1500)
            return;
        lastNotice = Environment.TickCount64;
        try
        {
            chat.PrintError("[Pet Service] Only chat messages and chat channel commands are available until you acknowledge or snooze.");
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
