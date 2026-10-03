using System.Text.Json;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using PetService.Core;
using PetService.Windows;

namespace PetService;
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IKeyState KeyState { get; private set; } = null!;
    [PluginService] internal static IGamepadState Gamepad { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider Interop { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IPlayerState Player { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;

    internal Configuration Configuration { get; private set; }
    private readonly WindowSystem windows = new("PetService");
    private readonly SettingsWindow settings;
    private readonly InputGuard input;
    private readonly NativeChat nativeChat;
    private readonly ChatCommandGuard chatCommands;
    private readonly MovementGuard movement;
    private readonly ServiceClient service = new();
    private readonly CancellationTokenSource cancel = new();
    private readonly MasterInputControl masterInput;
    private Task<SyncResult>? syncTask;
    private long syncRevision;
    private Task<PairResult>? pairTask;
    private string pairingUrl = "";
    private long nextSync, nextEvaluation, nextDisplayedAttempt;
    private bool previousLogin, localRelease, disposed;
    private string sessionId = "", loginAt = "", loginSource = "";
    internal string ServiceStatus { get; private set; } = "Not connected yet.";
    internal string SaveError { get; private set; } = "";
    internal string ScheduleError { get; private set; } = "";
    internal bool NetworkBusy => pairTask is not null || syncTask is not null;
    internal bool IsPaired => Configuration.DeviceToken.Length > 0 && !Configuration.Revoked;
    internal bool KillSwitchOn => !masterInput.Enabled;
    internal bool Ready => ClientState.IsLoggedIn && Player.IsLoaded && Objects.LocalPlayer is not null
        && !Condition[ConditionFlag.BetweenAreas] && !Condition[ConditionFlag.BetweenAreas51];
    internal DateTimeOffset Now => DateTimeOffset.UtcNow.AddMilliseconds(Configuration.ClockOffsetMilliseconds);
    internal Prompt? CurrentPrompt => masterInput.Enabled && Ready && !localRelease && IsPaired
        ? LocalState.Pending(Configuration.Prompts,Now) : null;
    internal bool HasVisibleReminder => CurrentPrompt is not null;
    internal bool InputCaptureUnavailable => input.HasFailed;
    internal bool MovementGuardUnavailable => !movement.Available;
    internal bool AutorunStopFailed => input.AutorunStopFailed;
    internal NativeChatState ChatState => chatCommands.Available && movement.Available ? nativeChat.Read() : default;

    public Plugin()
    {
        Configuration=PluginInterface.GetPluginConfig() as Configuration ?? new();
        Configuration.Reminders ??= []; Configuration.Prompts ??= []; Configuration.Outbox ??= [];
        masterInput=new(Configuration.Enabled);
        if(KillSwitchOn)ServiceStatus="Kill switch is on. Master input and status sharing are paused.";
        nativeChat=new(GameGui,Log);
        chatCommands=new(Interop,()=>HasVisibleReminder && input is not null && input.IsCapturing,Chat,Log);
        movement=new(Interop,()=>HasVisibleReminder && input is not null && input.IsCapturing,Log);
        input=new(KeyState,Gamepad,nativeChat,chatCommands,movement,Log);
        settings=new(this); windows.AddWindow(settings); windows.AddWindow(new PromptWindow(this));
        var ui=PluginInterface.UiBuilder;
        ui.DisableAutomaticUiHide=true;ui.DisableUserUiHide=true;ui.DisableCutsceneUiHide=true;ui.DisableGposeUiHide=true;
        ui.Draw+=Draw;ui.OpenConfigUi+=OpenSettings;ui.OpenMainUi+=OpenSettings;
        ClientState.Login+=OnLogin; Framework.Update+=Update;
        Commands.AddHandler("/petservice",new CommandInfo(Command) {HelpMessage="Open setup and kill switch. /petservice off: stop master input. /petservice on: resume. /petservice release: release for this login."});
        previousLogin=ClientState.IsLoggedIn;
        if(previousLogin)StartSession("first-observed");
        if(!IsPaired)settings.IsOpen=true;
    }
    private void OnLogin() {previousLogin=true;StartSession("login-event");nextSync=0;nextEvaluation=0;}
    private void StartSession(string source) {sessionId=Guid.NewGuid().ToString();loginAt=LocalState.Stamp(Now);loginSource=source;localRelease=false;}
    private void OpenSettings()=>settings.IsOpen=true;
    private void Command(string command,string args)
    {
        switch(args.Trim().ToLowerInvariant()) {
            case "off":SetEnabled(false);break;
            case "on":SetEnabled(true);break;
            case "release":ReleaseSession();break;
            default:OpenSettings();break;
        }
    }
    internal bool Mutate(Action<Configuration> change)
    {
        var before=JsonSerializer.Serialize(Configuration,ServiceClient.Json);
        try {change(Configuration);PluginInterface.SavePluginConfig(Configuration);SaveError="";return true;}
        catch {
            Configuration=JsonSerializer.Deserialize<Configuration>(before,ServiceClient.Json)!;
            SaveError="Could not save local state. The choice was not dismissed. Check disk space and plugin configuration permissions.";
            return false;
        }
    }
    internal void Pair(string url,string code)
    {
        if(NetworkBusy)return;
        try {ServiceClient.Endpoint(url,"/api/pair");pairingUrl=url.TrimEnd('/');pairTask=service.Pair(pairingUrl,code,cancel.Token);ServiceStatus="Pairing…";}
        catch(ServiceFailure e){ServiceStatus=e.Message;}
    }
    internal void ReleaseSession()
    {
        localRelease=true;input.Release();
        if(IsPaired && !KillSwitchOn)Mutate(c=>c.Outbox.Add(new Choice{Action="release",ClientAtUtc=LocalState.Stamp(Now)}));
        nextSync=0;
        if(!KillSwitchOn)ServiceStatus="Input guard released for this login. Pending reminders remain unresolved.";
    }
    internal void SetEnabled(bool enabled)
    {
        if(!enabled) {
            // Release first, including when disk persistence fails. Cancellation
            // alone is insufficient: CompleteNetwork also rejects old revisions.
            masterInput.SetEnabled(false);input.Release();
            var saved=Mutate(c=>c.Enabled=false);
            Configuration.Enabled=false;
            if(!saved)SaveError="The kill switch is ON for this session, but could not be saved. Fix local configuration permissions or disk space before reloading the plugin; it may resume after a reload.";
            ServiceStatus="Kill switch is on. Master input and status sharing are paused.";
            return;
        }
        if(masterInput.Enabled || !Mutate(c=>c.Enabled=true))return;
        masterInput.SetEnabled(true);localRelease=false;nextSync=0;nextEvaluation=0;
        ServiceStatus=IsPaired ? "Resuming connection… Pending prompts may return." : "Not paired. Enter the service URL and pairing code.";
    }
    internal void Unpair()
    {
        if(NetworkBusy)return;
        if(Mutate(c=>{c.DeviceToken="";c.PetId="";c.PetName="";c.Reminders.Clear();c.Prompts.Clear();c.Outbox.Clear();c.LastSyncAtUtc=null;c.ClockOffsetMilliseconds=0;c.Revoked=false;}))
        {input.Release();ServiceStatus="Unpaired. Request a new pairing code to reconnect.";}
    }
    internal void Choose(string action,string? reply=null)
    {
        var prompt=CurrentPrompt;if(prompt is null)return;
        var choice=new Choice{OccurrenceId=prompt.Id,Action=action,ClientAtUtc=LocalState.Stamp(Now),Minutes=action=="snooze"?10:null,Reply=reply};
        if(Mutate(c=>{LocalState.Apply(c.Prompts.Single(p=>p.Id==prompt.Id),choice);c.Outbox.Add(choice);})) {nextSync=0;if(!HasVisibleReminder)input.Release();}
    }
    internal void MarkDisplayed()
    {
        var prompt=CurrentPrompt;if(prompt is null || prompt.DisplayedAtUtc is not null || Environment.TickCount64<nextDisplayedAttempt)return;
        nextDisplayedAttempt=Environment.TickCount64+5000;
        var choice=new Choice{OccurrenceId=prompt.Id,Action="displayed",ClientAtUtc=LocalState.Stamp(Now)};
        Mutate(c=>{LocalState.Apply(c.Prompts.Single(p=>p.Id==prompt.Id),choice);c.Outbox.Add(choice);});
    }
    private Observation Observe()
    {
        bool logged=ClientState.IsLoggedIn, ready=Ready;
        var o=new Observation{LoggedIn=logged,Ready=ready,SessionId=logged?sessionId:"",LoginObservedAtUtc=logged?loginAt:"",LoginTimeSource=logged?loginSource:"",
            InputGuardActive=input.IsCapturing,LocalRelease=localRelease};
        if(!ready)return o;
        o.CharacterName=Player.CharacterName;o.HomeWorld=Player.HomeWorld.Value.Name.ToString();o.CurrentWorld=Player.CurrentWorld.Value.Name.ToString();
        o.DataCenter=Player.CurrentWorld.Value.DataCenter.Value.Name.ToString();o.TerritoryId=ClientState.TerritoryType;
        o.Zone=Data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(o.TerritoryId)?.PlaceName.Value.Name.ToString() ?? "Unknown";
        o.InDuty=Condition[ConditionFlag.BoundByDuty]||Condition[ConditionFlag.BoundByDuty56]||Condition[ConditionFlag.BoundByDuty95];
        o.InCombat=Condition[ConditionFlag.InCombat];o.IsAfk=Player.IsAwayFromKeyboard;o.GameIdle=ClientState.IsClientIdle();return o;
    }
    private void Update(IFramework framework)
    {
        if(disposed)return;
        try {
            var logged=ClientState.IsLoggedIn;
            if(logged && !previousLogin)StartSession("first-observed");
            if(!logged && previousLogin){localRelease=false;sessionId="";loginAt="";loginSource="";nextSync=0;input.Release();}
            previousLogin=logged;
            CompleteNetwork();
            if(Ready && masterInput.Enabled && IsPaired && Environment.TickCount64>=nextEvaluation) {
                nextEvaluation=Environment.TickCount64+1000;
                // Compute on a copy, then persist the whole change atomically.
                var prompts=JsonSerializer.Deserialize<List<Prompt>>(JsonSerializer.Serialize(Configuration.Prompts,ServiceClient.Json),ServiceClient.Json)!;
                if(LocalState.Refresh(Configuration.Reminders,prompts,Now) && !Mutate(c=>c.Prompts=prompts))Configuration.Prompts=prompts;
                ScheduleError="";
            }
            if(masterInput.Enabled && IsPaired && !NetworkBusy && Environment.TickCount64>=nextSync) {
                nextSync=Environment.TickCount64+5000;
                var batch=JsonSerializer.Deserialize<List<Choice>>(JsonSerializer.Serialize(Configuration.Outbox.Take(20),ServiceClient.Json),ServiceClient.Json)!;
                syncRevision=masterInput.Revision;
                syncTask=service.Sync(Configuration.ServiceUrl,Configuration.DeviceToken,Observe(),batch,masterInput.RequestCancellation);
            }
            if(HasVisibleReminder)input.SuppressKeyboardOnFrameworkUpdate();else input.Release();
        } catch {input.Release();ScheduleError="Could not evaluate the current schedule or game state. Open settings to inspect the connection; emergency release remains available.";}
    }
    private void CompleteNetwork()
    {
        if(pairTask is {IsCompleted:true}) {
            var task=pairTask;pairTask=null;
            try {
                var result=task.GetAwaiter().GetResult();
                if(!Guid.TryParse(result.PetId,out _) || result.Token.Length!=43)throw new ServiceFailure("Invalid pairing response.");
                if(Mutate(c=>{c.ServiceUrl=pairingUrl;c.DeviceToken=result.Token;c.PetId=result.PetId;c.PetName=result.PetName;c.Revoked=false;c.Reminders.Clear();c.Prompts.Clear();c.Outbox.Clear();c.ClockOffsetMilliseconds=0;}))
                {ServiceStatus=KillSwitchOn ? "Paired. Kill switch remains on; master input and status sharing are paused." : "Paired. Synchronizing…";nextSync=0;}
                else ServiceStatus="Pairing succeeded remotely, but the credential could not be saved. Fix the save problem and request a new !pair code.";
            } catch {ServiceStatus="Pairing failed. Check the HTTPS URL, code, and service availability. Request a fresh !pair code if necessary.";}
        }
        if(syncTask is {IsCompleted:true}) {
            var task=syncTask;syncTask=null;
            try {
                var result=task.GetAwaiter().GetResult();
                if(!masterInput.Accepts(syncRevision))return;
                if(result.PetId!=Configuration.PetId)throw new ServiceFailure("Profile mismatch.");
                var serverTime=LocalState.Parse(result.ServerTimeUtc);
                if(Mutate(c=>{
                    c.Outbox.RemoveAll(e=>result.AcceptedEventIds.Contains(e.Id));
                    c.Prompts=LocalState.Merge(c.Prompts,result,c.Outbox);c.Reminders=result.Reminders;
                    c.LastSyncAtUtc=result.ServerTimeUtc;c.ClockOffsetMilliseconds=(serverTime-DateTimeOffset.UtcNow).TotalMilliseconds;
                }))ServiceStatus="Connected.";
            } catch(ServiceFailure e) {
                if(!masterInput.Accepts(syncRevision))return;
                if(e.Revoked){Mutate(c=>c.Revoked=true);input.Release();ServiceStatus="Device authorization was revoked. Request a new pairing code.";}
                else {ServiceStatus="Service unavailable. Cached reminders and saved choices remain local.";nextSync=Environment.TickCount64+15000;}
            } catch {
                if(!masterInput.Accepts(syncRevision))return;
                ServiceStatus="Could not synchronize. Cached reminders and saved choices remain local.";nextSync=Environment.TickCount64+15000;
            }
        }
    }
    private void Draw()
    {try {windows.Draw();}catch{input.Release();}}
    internal void CaptureInput(bool editing,bool mouseOverChat)=>input.CaptureRenderedFrame(editing,mouseOverChat);
    internal void ReleaseInput()=>input.Release();
    public void Dispose()
    {
        disposed=true;cancel.Cancel();masterInput.Dispose();input.Release();
        PluginInterface.UiBuilder.Draw-=Draw;PluginInterface.UiBuilder.OpenConfigUi-=OpenSettings;PluginInterface.UiBuilder.OpenMainUi-=OpenSettings;
        ClientState.Login-=OnLogin;Framework.Update-=Update;Commands.RemoveHandler("/petservice");
        windows.RemoveAllWindows();chatCommands.Dispose();movement.Dispose();service.Dispose();cancel.Dispose();
        // A final logout heartbeat cannot be guaranteed during unload/crash. The
        // service marks observations stale after 45 seconds instead of assuming logout.
    }
}
