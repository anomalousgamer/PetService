using System.Text.Json;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Dalamud.Utility;
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

    [PluginService] internal static IDutyState Duty {get;private set;}=null!;
    [PluginService] internal static IGameInventory Inventory {get;private set;}=null!;
    [PluginService] internal static INotificationManager Notifications {get;private set;}=null!;
    private readonly ActivityRecorder recorder;
    private readonly TravelRecorder travel;
    private readonly UpdateNotifications updates=new();
    private readonly ChangesWindow changes;
    private long changesEligible;
    private bool changesShown;
    internal string ActivityError=>recorder.Error;
    internal Configuration Configuration { get; private set; }
    private readonly WindowSystem windows = new("PetService");
    private readonly SettingsWindow settings;
    private readonly KillSwitchWindow killConfirmation;
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
    private Task<DeviceEventResult>? safetyTask;
    private long nextSafetySync, nextAttentionRequest;
    private long nextSync, nextEvaluation, nextDisplayedAttempt;
    private long nextActivityFlush,lastSyncStarted,syncRetryUntil;
    private bool activityUploadRequested;
    private Observation? submittedObservation;
    private bool submittedActivity;
    private bool liveReportsRequested;
    private string? freshReportRequestId,submittedReportRequestId;
    private readonly ObservationUploadPolicy observationUpload=new();
    private bool previousLogin, localRelease, disposed;
    private string sessionId = "", loginAt = "", loginSource = "";
    internal string ServiceStatus { get; private set; } = "Not connected yet.";
    internal string SaveError { get; private set; } = "";
    internal string ScheduleError { get; private set; } = "";
    internal bool NetworkBusy => pairTask is not null || syncTask is not null || safetyTask is not null;
    internal bool IsPaired => Configuration.DeviceToken.Length > 0 && !Configuration.Revoked
        && ServiceClient.MatchesBundledService(Configuration.ServiceUrl);
    internal bool KillSwitchOn => !masterInput.Enabled;
    internal bool CanCallMaster=>IsPaired && !KillSwitchOn && Ready && Environment.TickCount64>=nextAttentionRequest;
    internal int CallCooldownSeconds=>(int)Math.Max(0,(nextAttentionRequest-Environment.TickCount64+999)/1000);
    internal void RequestObservationUpload()=>activityUploadRequested=true;
    internal void RecordTravel(Dictionary<string,object?> data,Observation context)=>recorder.Travel(data,context);
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
        Configuration.SafetyOutbox ??= [];
        Configuration.DisplayedChatIds ??=[];
        Configuration.ActivityOutbox ??=[];Configuration.Dynamic ??=new();
        masterInput=new(Configuration.Enabled);
        if(KillSwitchOn)ServiceStatus="Kill switch is on. Master input and status sharing are paused.";
        else if(Configuration.DeviceToken.Length>0 && !ServiceClient.MatchesBundledService(Configuration.ServiceUrl))
            ServiceStatus="This pairing belongs to a different service. Request a new pairing code to connect.";
        nativeChat=new(GameGui,Log);
        chatCommands=new(Interop,()=>HasVisibleReminder && input is not null && input.IsCapturing,Chat,Log);
        chatCommands.SpeechSettings=()=>IsPaired && !KillSwitchOn && Ready ? Configuration.Dynamic : null;
        recorder=new(this);travel=new(this);
        changes=new(this);windows.AddWindow(changes);
        movement=new(Interop,()=>HasVisibleReminder && input is not null && input.IsCapturing,Log);
        input=new(KeyState,Gamepad,nativeChat,chatCommands,movement,Log);
        settings=new(this); windows.AddWindow(settings); windows.AddWindow(new PromptWindow(this));
        killConfirmation=new(this); windows.AddWindow(killConfirmation);
        var ui=PluginInterface.UiBuilder;
        ui.DisableAutomaticUiHide=true;ui.DisableUserUiHide=true;ui.DisableCutsceneUiHide=true;ui.DisableGposeUiHide=true;
        ui.Draw+=Draw;ui.OpenConfigUi+=OpenSettings;ui.OpenMainUi+=OpenSettings;
        ClientState.Login+=OnLogin; Framework.Update+=Update;
        Commands.AddHandler("/petservice",new CommandInfo(Command) {HelpMessage="Open Pet/Master pages. /petservice off: confirm kill switch. /petservice on: resume. /petservice release: safe mode for this login. /petservice call: open Contact."});
        Commands.AddHandler("/toh",new CommandInfo(Command){HelpMessage="Open Pet Service. /toh call, off, on, release, changes, checkupdates."});
        previousLogin=ClientState.IsLoggedIn;
        if(previousLogin && IsPaired && !KillSwitchOn)StartSession("first-observed");
        if(!IsPaired)settings.IsOpen=true;
    }
    private void OnLogin() {previousLogin=true;if(IsPaired && !KillSwitchOn)StartSession("login-event");nextSync=0;nextEvaluation=0;}
    private void StartSession(string source) {sessionId=Guid.NewGuid().ToString();loginAt=LocalState.Stamp(Now);loginSource=source;localRelease=false;}
    private void OpenSettings()=>settings.IsOpen=true;
    internal void OpenContact()=>settings.OpenContact();
    private void Command(string command,string args)
    {
        switch(args.Trim().ToLowerInvariant()) {
            case "changes":changes.IsOpen=true;break;
            case "checkupdates":updates.CheckNow();break;
            case "off":SetEnabled(false);break;
            case "on":SetEnabled(true);break;
            case "release":ReleaseSession();break;
            case "call":OpenContact();break;
            default:if(args.Trim().Length==0)OpenSettings();else Chat.Print("Pet Service: unknown command. Use /toh to open the plugin or /toh call to open Contact.");break;
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
    internal void Pair(string code)
    {
        if(NetworkBusy)return;
        code=code.Trim();
        if(code.Length==0){ServiceStatus="Enter the one-time pairing code supplied by your master.";return;}
        pairTask=service.Pair(code,cancel.Token);ServiceStatus="Pairing…";
    }
    internal void ReleaseSession()
    {
        if(localRelease)return;
        localRelease=true;input.Release();
        if(IsPaired && !KillSwitchOn)Mutate(c=>c.SafetyOutbox.Add(new Choice{Action="release",ClientAtUtc=LocalState.Stamp(Now)}));
        nextSafetySync=0;
        nextSync=0;
        if(!KillSwitchOn)ServiceStatus="Input guard released for this login. Pending reminders remain unresolved.";
    }
    internal void SetEnabled(bool enabled)
    {
        if(!enabled) {
            if(!KillSwitchOn)killConfirmation.ShowConfirmation();
            return;
        }
        if(masterInput.Enabled || !Mutate(c=>c.Enabled=true))return;
        masterInput.SetEnabled(true);localRelease=false;nextSync=0;nextEvaluation=0;
        if(IsPaired && ClientState.IsLoggedIn)StartSession("first-observed");
        ServiceStatus=IsPaired ? "Resuming connection… Pending prompts may return." : "Not paired. Enter the one-time pairing code supplied by your master.";
    }
    internal void ConfirmKillSwitch()
    {
        if(KillSwitchOn)return;
        // Release first, including when disk persistence fails. Cancellation
        // alone is insufficient: CompleteNetwork also rejects old revisions.
        travel.Reset();recorder.Stop("kill switch activated");
        masterInput.SetEnabled(false);input.Release();sessionId="";loginAt="";loginSource="";
        var notice=IsPaired ? new Choice{Action="pause",ClientAtUtc=LocalState.Stamp(Now)} : null;
        var saved=Mutate(c=>{c.Enabled=false;if(notice is not null)c.SafetyOutbox.Add(notice);});
        Configuration.Enabled=false;
        if(!saved && notice is not null)Configuration.SafetyOutbox.Add(notice);
        if(!saved)SaveError="The kill switch is ON for this session, but could not be saved. Fix local configuration permissions or disk space before reloading the plugin; it may resume after a reload.";
        nextSafetySync=0;
        ServiceStatus="Kill switch is on. Master input and status sharing are paused.";
    }
    internal void RequestAttention(string? request=null)
    {
        if(!IsPaired || KillSwitchOn || !Ready){Chat.Print("Pet Service: pair and resume the plugin before requesting attention.");return;}
        if(Environment.TickCount64<nextAttentionRequest){Chat.Print("Pet Service: wait a minute between attention requests.");return;}
        if(Mutate(c=>c.Outbox.Add(new Choice{Action="attention",Request=request??"I'm requesting attention.",ClientAtUtc=LocalState.Stamp(Now)}))) {
            nextAttentionRequest=Environment.TickCount64+60000;nextSync=0;
            Chat.Print("Pet Service: Attention request queued for your master.");
        }
    }
    internal void Unpair()
    {
        if(NetworkBusy)return;
        recorder.Stop("device unpaired");
        if(Mutate(c=>{c.DisplayedChatIds.Clear();c.ActivityOutbox.Clear();c.Dynamic=new();c.ServiceUrl="";c.DeviceToken="";c.PetId="";c.PetName="";c.Reminders.Clear();c.Prompts.Clear();c.Outbox.Clear();c.SafetyOutbox.Clear();c.LastSyncAtUtc=null;c.ClockOffsetMilliseconds=0;c.Revoked=false;}))
        {recorder.ClearPairingState();input.Release();ServiceStatus="Unpaired. Request a new pairing code to reconnect.";}
    }
    internal void Choose(string action,string? reply=null,int? minutes=null,int? optionIndex=null)
    {
        var prompt=CurrentPrompt;if(prompt is null)return;
        if(action=="snooze" && !SnoozePolicy.IsValid(minutes))return;
        if(action=="choice" && !ReplyChoicePolicy.Allows(prompt,optionIndex))return;
        if(action=="reply" && (!prompt.AllowReply || string.IsNullOrWhiteSpace(reply)))return;
        var choice=new Choice{OccurrenceId=prompt.Id,Action=action,ClientAtUtc=LocalState.Stamp(Now),Minutes=action=="snooze"?minutes:null,Reply=reply,
            OptionIndex=action=="choice"?optionIndex:null};
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
        var zone=TimeZoneInfo.Local.Id;
        if(TimeZoneInfo.TryConvertWindowsIdToIanaId(zone,out var iana))zone=iana;
        var o=new Observation{TimeZone=zone,LoggedIn=logged,Ready=ready,SessionId=logged?sessionId:"",LoginObservedAtUtc=logged?loginAt:"",LoginTimeSource=logged?loginSource:"",
            InputGuardActive=input.IsCapturing,LocalRelease=localRelease};
        if(!Player.IsLoaded)return o;
        o.CharacterName=Player.CharacterName;o.HomeWorld=Player.HomeWorld.Value.Name.ToString();o.CurrentWorld=Player.CurrentWorld.Value.Name.ToString();
        if(!ready)return o;
        o.DataCenter=Player.CurrentWorld.Value.DataCenter.Value.Name.ToString();o.TerritoryId=ClientState.TerritoryType;
        var ordinaryZone=Data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(o.TerritoryId)?.PlaceName.Value.Name.ToString() ?? "Unknown";
        o.Zone=HousingLocation.Observe(o.TerritoryId,ordinaryZone);
        o.InDuty=Condition[ConditionFlag.BoundByDuty]||Condition[ConditionFlag.BoundByDuty56]||Condition[ConditionFlag.BoundByDuty95];
        o.MapId=ClientState.MapId;o.Job=Player.ClassJob.Value.Name.ToString();o.Level=Player.Level;
        if(Data.GetExcelSheet<Map>()?.GetRowOrDefault(o.MapId) is { } map && map.SizeFactor>0) {
            var pos=Objects.LocalPlayer!.Position;
            o.X=Math.Round(MapUtil.ConvertWorldCoordXZToMapCoord(pos.X,map.SizeFactor,map.OffsetX),2);
            o.Y=Math.Round(MapUtil.ConvertWorldCoordXZToMapCoord(pos.Z,map.SizeFactor,map.OffsetY),2);
        }
        o.Crafting=Condition[ConditionFlag.Crafting];o.Gathering=Condition[ConditionFlag.Gathering];o.Mounted=Condition[ConditionFlag.Mounted];
        o.Cutscene=Condition[ConditionFlag.WatchingCutscene]||Condition[ConditionFlag.WatchingCutscene78];o.Unconscious=Condition[ConditionFlag.Unconscious];
        o.InCombat=Condition[ConditionFlag.InCombat];o.IsAfk=Player.IsAwayFromKeyboard;o.GameIdle=ClientState.IsClientIdle();return o;
    }
    private void Update(IFramework framework)
    {
        if(disposed)return;
        try {
            var logged=ClientState.IsLoggedIn;
            if(logged && !previousLogin && IsPaired && !KillSwitchOn)StartSession("first-observed");
            if(!logged && previousLogin){localRelease=false;sessionId="";loginAt="";loginSource="";nextSync=0;input.Release();}
            previousLogin=logged;
            CompleteNetwork();
            recorder.Update(Observe);travel.Update(Observe);updates.Update();
            if(Ready && !changesShown) {
                if(changesEligible==0)changesEligible=Environment.TickCount64+3000;
                if(Environment.TickCount64>=changesEligible){changesShown=true;if(Configuration.LastAcknowledgedVersion!="0.3.0.0")changes.IsOpen=true;}
            } else if(!Ready)changesEligible=0;
            // Only explicit safety notices use this route. It carries no observation
            // and remains usable while the kill switch pauses normal synchronization.
            if(IsPaired && safetyTask is null && Configuration.SafetyOutbox.Count>0 && Environment.TickCount64>=nextSafetySync) {
                nextSafetySync=Environment.TickCount64+15000;
                var batch=JsonSerializer.Deserialize<List<Choice>>(JsonSerializer.Serialize(Configuration.SafetyOutbox.Take(20),ServiceClient.Json),ServiceClient.Json)!;
                safetyTask=service.DeviceEvents(Configuration.DeviceToken,batch,cancel.Token);
            }
            if(Ready && masterInput.Enabled && IsPaired && Environment.TickCount64>=nextEvaluation) {
                nextEvaluation=Environment.TickCount64+1000;
                // Compute on a copy, then persist the whole change atomically.
                var prompts=JsonSerializer.Deserialize<List<Prompt>>(JsonSerializer.Serialize(Configuration.Prompts,ServiceClient.Json),ServiceClient.Json)!;
                if(LocalState.Refresh(Configuration.Reminders,prompts,Now) && !Mutate(c=>c.Prompts=prompts))Configuration.Prompts=prompts;
                ScheduleError="";
            }
            var syncTick=Environment.TickCount64;
            if(masterInput.Enabled && IsPaired && !NetworkBusy && syncTick>=syncRetryUntil
                && syncTick>=lastSyncStarted+1000 && (syncTick>=nextSync || activityUploadRequested)) {
                nextSync=syncTick+5000;lastSyncStarted=syncTick;
                var batch=JsonSerializer.Deserialize<List<Choice>>(JsonSerializer.Serialize(Configuration.Outbox.Take(20),ServiceClient.Json),ServiceClient.Json)!;
                var flush=activityUploadRequested || syncTick>=nextActivityFlush || Configuration.ActivityOutbox.Count>=50;
                var activity=flush?Configuration.ActivityOutbox.Take(50).ToList():[];
                submittedActivity=activity.Count>0;
                if(flush){nextActivityFlush=syncTick+60000;activityUploadRequested=false;}
                var current=Observe();
                var reportQueueSaved=freshReportRequestId is null || recorder.FlushForReport();
                if(freshReportRequestId is not null){activity=Configuration.ActivityOutbox.Take(50).ToList();submittedActivity=activity.Count>0;}
                submittedObservation=observationUpload.NeedsSnapshot(current,syncTick,activity.Any(a=>a.Kind is not "interval" and not "position"),batch.Count>0,liveReportsRequested,freshReportRequestId is not null)?current:null;
                submittedReportRequestId=freshReportRequestId is not null && reportQueueSaved && Configuration.ActivityOutbox.Count<=50 && Configuration.Outbox.Count<=20?freshReportRequestId:null;
                syncRevision=masterInput.Revision;
                syncTask=service.Sync(Configuration.DeviceToken,submittedObservation,batch,activity,submittedReportRequestId,masterInput.RequestCancellation);
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
                if(Mutate(c=>{c.ServiceUrl=ServiceClient.BaseUrl;c.DeviceToken=result.Token;c.PetId=result.PetId;c.PetName=result.PetName;c.Revoked=false;c.DisplayedChatIds.Clear();c.ActivityOutbox.Clear();c.Dynamic=new();c.Reminders.Clear();c.Prompts.Clear();c.Outbox.Clear();c.SafetyOutbox.Clear();c.LastSyncAtUtc=null;c.ClockOffsetMilliseconds=0;}))
                {recorder.ClearPairingState();observationUpload.Reset();if(!KillSwitchOn && ClientState.IsLoggedIn)StartSession("first-observed");ServiceStatus=KillSwitchOn ? "Paired. Kill switch remains on; master input and status sharing are paused." : "Paired. Synchronizing…";nextSync=0;syncRetryUntil=0;}
                else ServiceStatus="Pairing succeeded remotely, but the credential could not be saved. Fix the save problem and request a new !pair code.";
            } catch {ServiceStatus="Pairing failed. Check the code and service availability. Request a fresh pairing code from your master if necessary.";}
        }
        if(safetyTask is {IsCompleted:true}) {
            var task=safetyTask;safetyTask=null;
            try {
                var result=task.GetAwaiter().GetResult();
                if(result.PetId!=Configuration.PetId)throw new ServiceFailure("Profile mismatch.");
                Mutate(c=>c.SafetyOutbox.RemoveAll(e=>result.AcceptedEventIds.Contains(e.Id)));
            } catch(ServiceFailure e) {
                if(e.Revoked)Mutate(c=>{c.Revoked=true;c.SafetyOutbox.Clear();});
                nextSafetySync=Environment.TickCount64+15000;
            } catch {nextSafetySync=Environment.TickCount64+15000;}
        }
        if(syncTask is {IsCompleted:true}) {
            var task=syncTask;syncTask=null;
            try {
                var result=task.GetAwaiter().GetResult();
                if(!masterInput.Accepts(syncRevision))return;
                if(result.PetId!=Configuration.PetId)throw new ServiceFailure("Profile mismatch.");
                var serverTime=LocalState.Parse(result.ServerTimeUtc);
                if(Mutate(c=>{
                    c.ActivityOutbox.RemoveAll(e=>result.AcceptedActivityIds.Contains(e.Id));c.Dynamic=result.Settings;
                    c.Outbox.RemoveAll(e=>result.AcceptedEventIds.Contains(e.Id));
                    c.Prompts=LocalState.Merge(c.Prompts,result,c.Outbox);c.Reminders=result.Reminders;
                    c.LastSyncAtUtc=result.ServerTimeUtc;c.ClockOffsetMilliseconds=(serverTime-DateTimeOffset.UtcNow).TotalMilliseconds;
                })){
                    if(submittedObservation is not null)observationUpload.Accepted(submittedObservation,Environment.TickCount64);
                    submittedObservation=null;syncRetryUntil=0;ServiceStatus="Connected.";
                    DeliverChat(result.ChatMessages);
                    var liveStarted=!liveReportsRequested && result.LiveReportsRequested;
                    liveReportsRequested=result.LiveReportsRequested;
                    freshReportRequestId=result.FreshReportRequestId;
                    if(freshReportRequestId is not null){recorder.FlushForReport();activityUploadRequested=true;nextSync=0;}
                    else if(liveStarted)nextSync=0;
                    if(submittedActivity && Configuration.ActivityOutbox.Count>0)activityUploadRequested=true;
                    if(Configuration.ActivityOutbox.Count>=50)nextSync=Environment.TickCount64+1000;
                }
            } catch(ServiceFailure e) {
                if(!masterInput.Accepts(syncRevision))return;
                observationUpload.Reset();
                if(e.Revoked){Mutate(c=>c.Revoked=true);input.Release();ServiceStatus="Device authorization was revoked. Request a new pairing code.";}
                else {ServiceStatus="Service unavailable. Cached reminders and saved choices remain local.";syncRetryUntil=nextSync=Environment.TickCount64+15000;activityUploadRequested=true;}
            } catch {
                if(!masterInput.Accepts(syncRevision))return;
                observationUpload.Reset();
                ServiceStatus="Could not synchronize. Cached reminders and saved choices remain local.";syncRetryUntil=nextSync=Environment.TickCount64+15000;activityUploadRequested=true;
            }
        }
    }
    private void DeliverChat(List<MasterChatMessage> messages)
    {
        if(KillSwitchOn || !IsPaired || !Ready)return;
        foreach(var message in messages) {
            if(!Guid.TryParse(message.Id,out _) || message.Text.Length is 0 or >1000 || message.Text.Any(c=>char.IsControl(c) && c is not '\n' and not '\t'))continue;
            if(message.ExpiresAtUtc is not null && LocalState.Parse(message.ExpiresAtUtc)<=Now)continue;
            if(Configuration.DisplayedChatIds.Contains(message.Id) || Configuration.Outbox.Any(e=>e.Action=="chat-displayed" && e.OccurrenceId=="c:"+message.Id))continue;
            try {MasterChatDelivery.Print(Configuration,message.Text);}catch {continue;}
            if(!Mutate(c=>{c.DisplayedChatIds.Add(message.Id);if(c.DisplayedChatIds.Count>500)c.DisplayedChatIds.RemoveAt(0);c.Outbox.Add(new(){Action="chat-displayed",OccurrenceId="c:"+message.Id,ClientAtUtc=LocalState.Stamp(Now)});}))break;
            nextSync=0;
        }
    }
    private void Draw()
    {try {windows.Draw();}catch{input.Release();}}
    internal void CaptureInput(bool editing,bool mouseOverChat)=>input.CaptureRenderedFrame(editing || settings.EditingText,mouseOverChat);
    internal void FocusGameChat(){settings.ClearTextFocus();nativeChat.FocusInput();}
    internal void ReleaseInput()=>input.Release();
    public void Dispose()
    {
        travel.Dispose();recorder.Dispose();disposed=true;cancel.Cancel();masterInput.Dispose();input.Release();
        PluginInterface.UiBuilder.Draw-=Draw;PluginInterface.UiBuilder.OpenConfigUi-=OpenSettings;PluginInterface.UiBuilder.OpenMainUi-=OpenSettings;
        ClientState.Login-=OnLogin;Framework.Update-=Update;Commands.RemoveHandler("/petservice");Commands.RemoveHandler("/toh");
        settings.Dispose();windows.RemoveAllWindows();chatCommands.Dispose();movement.Dispose();service.Dispose();cancel.Dispose();
        // A final logout heartbeat cannot be guaranteed during unload/crash. The
        // service marks observations stale after 45 seconds instead of assuming logout.
    }
}
