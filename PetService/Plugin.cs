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

    [PluginService] internal static IUnlockState Unlocks {get;private set;}=null!;
    [PluginService] internal static IDutyState Duty {get;private set;}=null!;
    [PluginService] internal static IGameInventory Inventory {get;private set;}=null!;
    [PluginService] internal static INotificationManager Notifications {get;private set;}=null!;
    [PluginService] internal static ITextureProvider Textures {get;private set;}=null!;
    private readonly IntegrationBridge integrations;
    internal readonly V6Controller V6;
    internal readonly PrivateChatWindow PrivateChat;
    internal readonly MasterSoundsWindow MasterSounds;
    internal bool DrawingPrivateChat {get;set;}
    internal bool PrivateChatFocused=>PrivateChat is not null&&PrivateChat.IsOpen&&PrivateChat.EditingText&&!PrivateChat.Session.Paused;
    internal readonly RemoteSounds Sounds;
    internal void RequestV6Sync()=>nextSync=0;
    internal DynamicSettings EffectiveSpeech=>V6.EffectiveSpeech();
    private Task<CredentialState>? credentialTask;
    private string credentialToken="";
    private string observedCharacter="";
    private long characterRevision,syncCharacterRevision;
    private bool inventoryDirty=true;
    private long inventoryAfter;
    private InventorySnapshot? pendingInventory;
    private string submittedInventoryAt="";
    private object? submittedCatalog;
    private List<FeatureResult> submittedResults=[];
    private string inventoryRequestId="";
    internal string MasterName=>Configuration.Features.Identity.Name;
    internal string CurrentCharacterId=>ClientState.IsLoggedIn && Player.IsLoaded && Player.ContentId!=0?Player.ContentId.ToString(System.Globalization.CultureInfo.InvariantCulture):"";
    internal bool CharacterAllowed=>CurrentCharacterId.Length>0 && Configuration.Characters.Any(c=>c.Id==CurrentCharacterId && c.Allowed);
    internal bool SharingAllowed=>!disposed && IsPaired && !KillSwitchOn && CharacterAllowed;
    internal bool LocalRelease=>localRelease;
    internal bool GuardAvailable=>movement.Available && chatCommands.Available && nativeChat.Read().Available && !input.HasFailed;
    internal bool GuardRequested=>SharingAllowed && Ready && !localRelease && (Sleeping || HasVisibleReminder || LockRequested) && GuardAvailable;
    // Input detours consume capture state. Calling GuardRequested here would
    // resample native input and prompt selection from inside an input query.
    private bool GameplayCaptureActive=>input is not null && input.IsCapturing && SharingAllowed && Ready && !localRelease;
    internal bool LockRequested=>Configuration.Features.Lock is {Enabled:true} l && (l.ExpiresAtUtc is null || LocalState.Parse(l.ExpiresAtUtc)>Now);
    internal Task<byte[]> ReadPortrait()=>service.Portrait(Configuration.DeviceToken,cancel.Token);
    internal void MarkInventoryDirty(){inventoryDirty=true;inventoryAfter=Environment.TickCount64+2000;}
    internal void SetCharacterAccess(string id,bool allowed){if(!Mutate(c=>{var x=c.Characters.Single(x=>x.Id==id);x.Allowed=allowed;}))return;characterRevision++;observedCharacter="";input.Release();integrations.Release();nextSync=0;observationUpload.Reset();}
    internal void TaskRespond(PetTask task,string state,string comment,List<bool> checklist){if(!SharingAllowed)return;var e=new TaskEvent{TaskId=task.Id,Revision=task.Revision,State=state,Comment=comment,Checklist=checklist.ToList()};if(Mutate(c=>{c.TaskEvents.Add(e);var t=c.Features.Tasks.First(x=>x.Id==task.Id);t.State=state;t.Comment=comment;for(var i=0;i<t.Checklist.Count&&i<e.Checklist.Count;i++)t.Checklist[i].Done=e.Checklist[i];}))nextSync=0;}
    private static string RecoveryDirectory=>Path.Combine(PluginInterface.GetPluginConfigDirectory(),"recovery");
    private static void ArchiveRecovery(Configuration config){if(config.Recovery.Count<=5000)return;Directory.CreateDirectory(RecoveryDirectory);var overflow=config.Recovery.Take(config.Recovery.Count-5000).ToList();foreach(var record in overflow){var json=JsonSerializer.Serialize(record,ServiceClient.Json);var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))).ToLowerInvariant();var file=Path.Combine(RecoveryDirectory,hash+".json");File.WriteAllText(file,json);}config.Recovery.RemoveRange(0,overflow.Count);}
    internal int ArchivedRecoveryCount {get{try{return Directory.Exists(RecoveryDirectory)?Directory.EnumerateFiles(RecoveryDirectory,"*.json").Count():0;}catch{return 0;}}}
    internal void RetryRecovery(){if(!SharingAllowed)return;var archives=new List<(string Path,RecoveryRecord Record)>();try{if(Directory.Exists(RecoveryDirectory))foreach(var file in Directory.EnumerateFiles(RecoveryDirectory,"*.json").Take(100)){var r=JsonSerializer.Deserialize<RecoveryRecord>(File.ReadAllText(file),ServiceClient.Json);if(r is not null && r.PetId==Configuration.PetId)archives.Add((file,r));}}catch{}
        var saved=Mutate(c=>{foreach(var entry in archives)if(!c.Recovery.Any(r=>r.Id==entry.Record.Id&&r.Payload==entry.Record.Payload))c.Recovery.Add(entry.Record);foreach(var r in c.Recovery.Where(x=>x.PetId==c.PetId).ToList())try{if(r.Kind=="activity"){var e=JsonSerializer.Deserialize<ActivityRecord>(r.Payload,ServiceClient.Json)!;if(!c.Characters.Any(x=>x.Id==e.CharacterId&&x.Allowed))continue;if(!c.ActivityOutbox.Any(x=>x.Id==e.Id))c.ActivityOutbox.Add(e);}else if(r.Kind=="reply"){var e=JsonSerializer.Deserialize<Choice>(r.Payload,ServiceClient.Json)!;if(!c.Outbox.Any(x=>x.Id==e.Id))c.Outbox.Add(e);}else if(r.Kind=="control-result"){var e=JsonSerializer.Deserialize<FeatureResult>(r.Payload,ServiceClient.Json)!;if(e.AtUtc is not null)e.AtUtc=LocalState.Stamp(LocalState.Parse(e.AtUtc));if(!c.FeatureResults.Any(x=>x.Id==e.Id&&x.State==e.State))c.FeatureResults.Add(e);}else if(r.Kind=="interaction"){var e=JsonSerializer.Deserialize<AdvancedEvent>(r.Payload,ServiceClient.Json)!;if(!c.V6Outbox.Any(x=>x.Id==e.Id))c.V6Outbox.Add(e);}else if(r.Kind=="task"){var e=JsonSerializer.Deserialize<TaskEvent>(r.Payload,ServiceClient.Json)!;if(!c.TaskEvents.Any(x=>x.Id==e.Id))c.TaskEvents.Add(e);}else continue;c.Recovery.Remove(r);}catch{}});if(saved)foreach(var entry in archives.Where(a=>!Configuration.Recovery.Any(r=>r.Id==a.Record.Id)))try{File.Delete(entry.Path);}catch{}nextSync=0;}
    private readonly ActivityRecorder recorder;
    private readonly TravelRecorder travel;
    private readonly UpdateNotifications updates;
    private readonly ChangesWindow changes;
    private long changesEligible;
    private bool changesShown;
    internal string ActivityError=>recorder.Error;
    internal Configuration Configuration { get; private set; }
    private readonly WindowSystem windows = new("PetService");
    private readonly SettingsWindow settings;
    private readonly KillSwitchWindow killConfirmation;
    private readonly SleepController sleep;
    private readonly SleepInputGuard sleepInput;
    private readonly ChatTwoBridge chatTwo;
    internal bool Sleeping=>sleep is not null && sleep.Active;
    internal bool InputGuardCapturing=>input.IsCapturing;
    internal void EmergencyWake()=>sleep.End("Emergency Wake Up selected by pet.");
    internal void RequestFeatureSync()=>nextSync=0;
    internal void ReleaseInputImmediately()=>input.Release();
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
    private PairingCharacter? pairingCharacter;
    private Task<DeviceEventResult>? safetyTask;
    private List<FeatureResult> submittedSafetyResults=[];
    private long nextSafetySync, nextAttentionRequest;
    private long nextSync, nextEvaluation, nextDisplayedAttempt;
    private long nextActivityFlush,lastSyncStarted,syncRetryUntil;
    private bool activityUploadRequested;
    private Observation? submittedObservation;
    private bool submittedActivity;
    private bool liveReportsRequested;
    private string? freshReportRequestId,submittedReportRequestId;
    private readonly ObservationUploadPolicy observationUpload=new();
    private readonly FrameStepRunner frameSteps=new();
    internal string UpdateError=>frameSteps.FailedSteps.Count==0?"":"Local updates are retrying ("+string.Join(", ",frameSteps.FailedSteps)+"). Details are in the Dalamud log (/xllog).";
    private bool previousLogin, localRelease, disposed;
    private readonly ShutdownSequence shutdown = new();
    private string sessionId = "", loginAt = "", loginSource = "";
    internal string ServiceStatus { get; private set; } = "Not connected yet.";
    internal string SaveError { get; private set; } = "";
    internal string ScheduleError { get; private set; } = "";
    internal bool NetworkBusy => pairTask is not null || syncTask is not null || safetyTask is not null;
    internal bool IsPaired => Configuration.DeviceToken.Length > 0 && !Configuration.Revoked
        && ServiceClient.MatchesBundledService(Configuration.ServiceUrl);
    internal bool KillSwitchOn => !masterInput.Enabled;
    internal string ObservedSessionId=>sessionId;
    internal bool CanCallMaster=>SharingAllowed && Ready && Environment.TickCount64>=nextAttentionRequest;
    internal int CallCooldownSeconds=>(int)Math.Max(0,(nextAttentionRequest-Environment.TickCount64+999)/1000);
    internal void RequestObservationUpload()=>activityUploadRequested=true;
    internal void RecordTravel(Dictionary<string,object?> data,Observation context)=>recorder.Travel(data,context);
    internal bool Ready => ClientState.IsLoggedIn && Player.IsLoaded && Objects.LocalPlayer is not null
        && !Condition[ConditionFlag.BetweenAreas] && !Condition[ConditionFlag.BetweenAreas51];
    internal DateTimeOffset Now => DateTimeOffset.UtcNow.AddMilliseconds(Configuration.ClockOffsetMilliseconds);
    internal AdvancedPrompt? CurrentAdvanced=>SharingAllowed&&Ready&&!localRelease&&!Sleeping?V6Local.Pending(Configuration.Features.V6,Now,ReadPromptContext()):null;
    // Prompt selection needs only these game flags, never the full report:
    // Observe() itself includes whether a prompt is currently visible.
    private Observation ReadPromptContext()=>new()
    {
        InDuty=Condition[ConditionFlag.BoundByDuty]||Condition[ConditionFlag.BoundByDuty56]||Condition[ConditionFlag.BoundByDuty95],
        InCombat=Condition[ConditionFlag.InCombat],
        Cutscene=Condition[ConditionFlag.WatchingCutscene]||Condition[ConditionFlag.WatchingCutscene78],
    };
    internal Prompt? CurrentPrompt {
        get {
            if(!SharingAllowed||!Ready||localRelease||Sleeping)return null;
            var old=LocalState.Pending(Configuration.Prompts,Now);var advanced=CurrentAdvanced;
            if(advanced is null||old is not null&&advanced.Policy.Priority==0&&string.CompareOrdinal(old.DueAtUtc,advanced.SendAtUtc)<=0)return old;
            return new(){Id=advanced.Id,Kind=advanced.RoutineId is null?"message":"reminder",Label=advanced.Title,Text=advanced.Text,DueAtUtc=advanced.SendAtUtc,Responses=advanced.Responses,AllowReply=advanced.Responses.AllowReply||advanced.Responses.Options.Any(o=>o.Action=="open-reply"),DisplayedAtUtc=advanced.DisplayedAtUtc,Advanced=true,Policy=advanced.Policy};
        }
    }
    internal bool HasVisibleReminder=>CurrentPrompt is not null;
    internal bool InputCaptureUnavailable => input.HasFailed;
    internal bool MovementGuardUnavailable => !movement.Available;
    internal bool AutorunStopFailed => input.AutorunStopFailed;
    internal NativeChatState ChatState => chatCommands.Available && movement.Available ? nativeChat.Read() : default;

    public Plugin()
    {
        var savedConfiguration=PluginInterface.GetPluginConfig() as Configuration;
        Configuration=savedConfiguration ?? new();
        Configuration.V6Outbox??=[];Configuration.V6ChatIds??=[];Configuration.PointNoticeIds??=[];Configuration.RedemptionNoticeIds??=[];Configuration.EndedSleepIds??=[];Configuration.Characters??=[];Configuration.Features??=new();Configuration.Features.V6??=new();Configuration.FeatureResults??=[];Configuration.TaskEvents??=[];Configuration.Recovery??=[];
        Configuration.ManagedTitles??=[];Configuration.PreviousTitles??=[];Configuration.ManagedMoodles??=[];Configuration.IntegrationLeases??=[];Configuration.IntegrationCleanupCharacters??=[];
        Configuration.Reminders ??= []; Configuration.Prompts ??= []; Configuration.Outbox ??= [];
        Configuration.SafetyOutbox ??= [];
        Configuration.PrivateChatVaults??=[];Configuration.AudioResults??=[];Configuration.AudioEvents??=[];Configuration.SoundPromptReturns??=[];Configuration.SeenSoundRequests??=[];Configuration.ChatWindow??=new();
        if(!Configuration.AudioMigrated)Mutate(c=>{c.MasterChatSound=false;c.AudioMigrated=true;});
        Configuration.DisplayedChatIds ??=[];
        Configuration.ActivityOutbox ??=[];Configuration.Dynamic ??=new();
        Configuration.Dynamic.NormalizeContacts();
        if(Configuration.ChatColourRevision<1) {
            Mutate(c=>{if(c.MasterChatColour==45)c.MasterChatColour=540;if(c.MasterChatColour==541)c.MasterChatColour=561;c.ChatColourRevision=1;});
        }
        updates=new(this,savedConfiguration is not null);
        integrations=new(this);
        if(Configuration.DeviceToken.Length>0 && Configuration.Revoked && !Configuration.RevocationConfirmed){credentialToken=Configuration.DeviceToken;credentialTask=service.Verify(credentialToken,cancel.Token);}
        if(!Configuration.CharacterAccessMigrated){Mutate(c=>{foreach(var e in c.ActivityOutbox){c.Recovery.Add(new(){Id=e.Id,Kind="legacy",Code="CHARACTER_PROVENANCE_REQUIRED",Payload=JsonSerializer.Serialize(e,ServiceClient.Json),PetId=c.PetId});}c.ActivityOutbox.Clear();ArchiveRecovery(c);c.CharacterAccessMigrated=true;c.QueuesPetId=c.PetId;});}
        masterInput=new(Configuration.Enabled);
        if(KillSwitchOn)ServiceStatus=$"Kill switch is on. {MasterName} input and status sharing are paused.";
        else if(Configuration.DeviceToken.Length>0 && !ServiceClient.MatchesBundledService(Configuration.ServiceUrl))
            ServiceStatus="This pairing belongs to a different service. Request a new pairing code to connect.";
        V6=new(this);nativeChat=new(GameGui,Log);chatTwo=new(PluginInterface);sleep=new(this);sleepInput=new(Interop,()=>Sleeping,Log);
        chatCommands=new(Interop,()=>GameplayCaptureActive,Chat,Log);
        chatCommands.Sleeping=()=>Sleeping;
        chatCommands.SpeechSettings=()=>SharingAllowed && Ready ? Configuration.Dynamic : null;
        recorder=new(this);travel=new(this);
        changes=new(this);windows.AddWindow(changes);
        movement=new(Interop,()=>GameplayCaptureActive,Log);
        input=new(KeyState,Gamepad,nativeChat,chatCommands,movement,chatTwo,()=>Sleeping,Log);
        PrivateChat=new(this);Sounds=new(this);MasterSounds=new(this);windows.AddWindow(PrivateChat);windows.AddWindow(MasterSounds);
        sleepInput.DrawingPrivateChat=()=>DrawingPrivateChat;
        sleepInput.PrivateChatFocused=()=>PrivateChatFocused;
        input.PrivateChatFocused=()=>PrivateChatFocused;
        settings=new(this); windows.AddWindow(settings); windows.AddWindow(new PromptWindow(this));
        killConfirmation=new(this); windows.AddWindow(killConfirmation);windows.AddWindow(new SleepWindow(this));windows.AddWindow(new PointToastWindow(this){IsOpen=true});
        var ui=PluginInterface.UiBuilder;
        ui.DisableAutomaticUiHide=true;ui.DisableUserUiHide=true;ui.DisableCutsceneUiHide=true;ui.DisableGposeUiHide=true;
        ui.Draw+=Draw;ui.OpenConfigUi+=OpenSettings;ui.OpenMainUi+=OpenSettings;
        ClientState.Login+=OnLogin; Framework.Update+=Update;
        Commands.AddHandler("/petservice",new CommandInfo(Command) {HelpMessage="Open Pet/Master pages. /petservice off: confirm kill switch. /petservice on: resume. /petservice release: safe mode for this login. /petservice call: open Contact."});
        Commands.AddHandler("/toh",new CommandInfo(Command){HelpMessage="Open Pet Service. /toh chat, call, off, on, release, changes, checkupdates."});
        previousLogin=ClientState.IsLoggedIn;
        if(previousLogin && SharingAllowed)StartSession("first-observed");
    }
    private void OnLogin() {if(disposed)return;previousLogin=true;if(SharingAllowed)StartSession("login-event");nextSync=0;nextEvaluation=0;}
    private void StartSession(string source) {sessionId=Guid.NewGuid().ToString();loginAt=LocalState.Stamp(Now);loginSource=source;localRelease=false;}
    private void OpenSettings()=>settings.IsOpen=true;
    internal void OpenContact()=>settings.OpenContact();
    private void Command(string command,string args)
    {
        switch(args.Trim().ToLowerInvariant()) {
            case "chat":PrivateChat.OpenDefault();break;
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
        if(CurrentCharacterId.Length==0){ServiceStatus="Log in to your character before pairing.";return;}
        code=code.Trim();
        if(code.Length==0){ServiceStatus=$"Enter the one-time pairing code supplied by {MasterName}.";return;}
        pairingCharacter=new(CurrentCharacterId,Player.CharacterName,Player.HomeWorld.Value.Name.ToString());
        pairTask=service.Pair(code,cancel.Token);ServiceStatus="Pairing…";
    }
    private void AdvanceAccessRevision()
    {
        var revision=checked(Math.Max(0,Configuration.AccessRevision)+1);
        Mutate(c=>c.AccessRevision=revision);
        // Keep the ordering barrier in memory even when a local save fails.
        Configuration.AccessRevision=revision;
    }
    internal void ReleaseSession()
    {
        if(localRelease)return;
        localRelease=true;input.Release();masterInput.Invalidate();AdvanceAccessRevision();
        ReleaseCleanup("Sleep",()=>sleep.End("Safe mode selected by pet."));
        ReleaseCleanup("integrations",integrations.Release);
        if(IsPaired && !KillSwitchOn)Mutate(c=>c.SafetyOutbox.Add(new Choice{Action="release",AccessRevision=Configuration.AccessRevision,ClientAtUtc=LocalState.Stamp(Now)}));
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
        if(masterInput.Enabled || !Mutate(c=>{c.Enabled=true;c.AccessRevision=checked(Math.Max(0,c.AccessRevision)+1);}))return;
        masterInput.SetEnabled(true);localRelease=false;nextSync=0;nextEvaluation=0;
        if(SharingAllowed)StartSession("first-observed");
        ServiceStatus=IsPaired ? "Resuming connection… Pending prompts may return." : $"Not paired. Enter the one-time pairing code supplied by {MasterName}.";
    }
    internal void ConfirmKillSwitch()
    {
        if(KillSwitchOn)return;
        // Release first, including when disk persistence fails. Cancellation
        // alone is insufficient: CompleteNetwork also rejects old revisions.
        masterInput.SetEnabled(false);input.Release();sessionId="";loginAt="";loginSource="";
        Configuration.Enabled=false;
        var revision=checked(Math.Max(0,Configuration.AccessRevision)+1);
        var notice=IsPaired ? new Choice{Action="pause",AccessRevision=revision,ClientAtUtc=LocalState.Stamp(Now)} : null;
        var saved=Mutate(c=>{c.Enabled=false;c.AccessRevision=revision;if(notice is not null)c.SafetyOutbox.Add(notice);});
        Configuration.Enabled=false;Configuration.AccessRevision=revision;
        if(!saved && notice is not null)Configuration.SafetyOutbox.Add(notice);
        if(!saved)SaveError="The kill switch is ON for this session, but could not be saved. Fix local configuration permissions or disk space before reloading the plugin; it may resume after a reload.";
        nextSafetySync=0;
        ServiceStatus=$"Kill switch is on. {MasterName} input and status sharing are paused.";
        ReleaseCleanup("Sleep",()=>sleep.End("Kill switch selected by pet."));
        ReleaseCleanup("travel",travel.Reset);
        ReleaseCleanup("activity",()=>recorder.Stop("kill switch activated"));
        ReleaseCleanup("integrations",integrations.Release);
        ReleaseCleanup("sounds",Sounds.Stop);
    }
    private static void ReleaseCleanup(string name,System.Action cleanup)
    {
        try { cleanup(); }
        catch(Exception error) { try { Log.Warning(error,"Pet Service local release cleanup failed: {Component}",name); } catch { } }
    }
    internal void RequestAttention(string? request=null)
    {
        if(!SharingAllowed || !Ready){Chat.Print("Pet Service: pair and resume the plugin before requesting attention.");return;}
        if(Environment.TickCount64<nextAttentionRequest){Chat.Print("Pet Service: wait a minute between attention requests.");return;}
        if(Mutate(c=>c.Outbox.Add(new Choice{Action="attention",Request=request??"I'm requesting attention.",ClientAtUtc=LocalState.Stamp(Now)}))) {
            nextAttentionRequest=Environment.TickCount64+60000;nextSync=0;
            Chat.Print("Pet Service: Attention request queued for "+MasterName+".");
        }
    }
    internal void Unpair()
    {
        if(NetworkBusy)return;
        sleep.End("Device unpaired.");integrations.Release();
        recorder.Stop("device unpaired");
        if(Mutate(c=>{c.V6Outbox.Clear();c.V6ChatIds.Clear();c.PointNoticeIds.Clear();c.RedemptionNoticeIds.Clear();c.DisplayedChatIds.Clear();c.ActivityOutbox.Clear();c.Features=new();c.FeatureResults.Clear();c.TaskEvents.Clear();c.Dynamic=new();c.ServiceUrl="";c.DeviceToken="";c.PetId="";c.PetName="";c.Reminders.Clear();c.Prompts.Clear();c.Outbox.Clear();c.SafetyOutbox.Clear();c.LastSyncAtUtc=null;c.ClockOffsetMilliseconds=0;c.Revoked=false;}))
        {recorder.ClearPairingState();input.Release();ServiceStatus="Unpaired. Request a new pairing code to reconnect.";}
    }
    internal void Choose(string action,string? reply=null,int? minutes=null,int? optionIndex=null,string? optionId=null,string? snoozeMode=null)
    {
        var prompt=CurrentPrompt;if(prompt is null)return;
        if(prompt.Advanced&&CurrentAdvanced is { } advanced) {
            var option=advanced.Responses.Options.FirstOrDefault(o=>o.Id==optionId);
            if(action=="snooze"&&(option is null||option.Action is not("snooze" or "random-snooze")||minutes is null||minutes<(option.Action=="random-snooze"?1:advanced.Policy.SnoozeMin)||minutes>(option.Action=="random-snooze"?10:advanced.Policy.SnoozeMax)||advanced.Policy.SnoozeLimit>0&&advanced.SnoozeCount>=advanced.Policy.SnoozeLimit))return;
            if(action=="reply"&&(!prompt.AllowReply||string.IsNullOrWhiteSpace(reply)))return;
            if(action=="response"&&option is null)return;
            V6.Queue(new(){PromptId=advanced.Id,Revision=advanced.Revision,Action=action,OptionId=optionId,Minutes=minutes,Reply=reply});
            if(action=="snooze")Chat.Print(option?.Action=="random-snooze"?"Pet Service: Reminder snoozed.":"Pet Service: Snoozed for "+minutes+" minutes.");return;
        }
        if(action=="snooze" && !SnoozePolicy.IsValid(minutes))return;
        if(action=="choice" && !ReplyChoicePolicy.Allows(prompt,optionIndex))return;
        if(action=="reply" && (!prompt.AllowReply || string.IsNullOrWhiteSpace(reply)))return;
        if(prompt.Kind=="reminder") {
            var option=prompt.Responses?.Options.FirstOrDefault(o=>o.Id==optionId);
            if(action=="response" && option is null)return;
            if(action=="snooze" && (option is null || option.Action is not ("snooze" or "random-snooze") || minutes is <1 or >10))return;
        }
        var choice=new Choice{ResponseRevision=prompt.Responses?.Revision,OptionId=optionId,SnoozeMode=snoozeMode,OccurrenceId=prompt.Id,Action=action,ClientAtUtc=LocalState.Stamp(Now),Minutes=action=="snooze"?minutes:null,Reply=reply,
            OptionIndex=action=="choice"?optionIndex:null};
        if(Mutate(c=>{LocalState.Apply(c.Prompts.Single(p=>p.Id==prompt.Id),choice);c.Outbox.Add(choice);})) {if(action=="snooze"&&prompt.Kind=="reminder")Chat.Print(snoozeMode=="random-snooze"?"Pet Service: Reminder snoozed.":$"Pet Service: Reminder snoozed for {minutes} minutes.");nextSync=0;if(!HasVisibleReminder)input.Release();}
    }
    internal void MarkDisplayed()
    {
        var prompt=CurrentPrompt;if(prompt is null || prompt.DisplayedAtUtc is not null || Environment.TickCount64<nextDisplayedAttempt)return;
        nextDisplayedAttempt=Environment.TickCount64+5000;
        if(prompt.Advanced&&CurrentAdvanced is { } advanced){V6.Queue(new(){PromptId=advanced.Id,Revision=advanced.Revision,Action="displayed"});return;}
        var choice=new Choice{ResponseRevision=prompt.Responses?.Revision,OccurrenceId=prompt.Id,Action="displayed",ClientAtUtc=LocalState.Stamp(Now)};
        Mutate(c=>{LocalState.Apply(c.Prompts.Single(p=>p.Id==prompt.Id),choice);c.Outbox.Add(choice);});
    }
    internal Observation Observe()
    {
        bool logged=ClientState.IsLoggedIn, ready=Ready;
        var zone=TimeZoneInfo.Local.Id;
        if(TimeZoneInfo.TryConvertWindowsIdToIanaId(zone,out var iana))zone=iana;
        var o=new Observation{LocalCharacterId=CurrentCharacterId,TimeZone=zone,LoggedIn=logged,Ready=ready,SessionId=logged?sessionId:"",LoginObservedAtUtc=logged?loginAt:"",LoginTimeSource=logged?loginSource:"",
            InputGuardActive=input.IsCapturing,LocalRelease=localRelease};
        if(!Player.IsLoaded)return o;
        o.CharacterName=Player.CharacterName;o.HomeWorld=Player.HomeWorld.Value.Name.ToString();o.CurrentWorld=Player.CurrentWorld.Value.Name.ToString();
        if(!ready)return o;
        o.DataCenter=Player.CurrentWorld.Value.DataCenter.Value.Name.ToString();o.TerritoryId=ClientState.TerritoryType;
        var ordinaryZone=Data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(o.TerritoryId)?.PlaceName.Value.Name.ToString() ?? "Unknown";
        o.Zone=HousingLocation.Observe(o.TerritoryId,ordinaryZone);
        var promptContext=ReadPromptContext();o.InDuty=promptContext.InDuty;
        o.MapId=ClientState.MapId;o.Job=Player.ClassJob.Value.Name.ToString();o.Level=Player.Level;
        if(Data.GetExcelSheet<Map>()?.GetRowOrDefault(o.MapId) is { } map && map.SizeFactor>0) {
            var pos=Objects.LocalPlayer!.Position;
            o.X=Math.Round(MapUtil.ConvertWorldCoordXZToMapCoord(pos.X,map.SizeFactor,map.OffsetX),2);
            o.Y=Math.Round(MapUtil.ConvertWorldCoordXZToMapCoord(pos.Z,map.SizeFactor,map.OffsetY),2);
        }
        o.Crafting=Condition[ConditionFlag.Crafting];o.Gathering=Condition[ConditionFlag.Gathering];o.Mounted=Condition[ConditionFlag.Mounted];
        o.Cutscene=promptContext.Cutscene;o.Unconscious=Condition[ConditionFlag.Unconscious];
        o.Bonded=LockRequested && input.IsCapturing;o.Sleeping=Sleeping && input.IsCapturing;o.PromptActive=input.IsCapturing && HasVisibleReminder;
        o.InCombat=promptContext.InCombat;o.IsAfk=Player.IsAwayFromKeyboard;o.GameIdle=ClientState.IsClientIdle();return o;
    }
    private string reportedLock="",reportedLockState="";
    private void UpdateLockResult(){var l=Configuration.Features.Lock;if(l is null || !SharingAllowed || !Ready)return;var state=l.State=="expired"?"expired":!l.Enabled?l.State is "failed" or "unavailable"?l.State:"released":!LockRequested?"expired":localRelease?"released":input.HasFailed||!movement.Available||!chatCommands.Available||!nativeChat.Read().Available?"failed":input.IsCapturing?"applied":"queued";if(state=="queued"||reportedLock==l.Id&&reportedLockState==state)return;if(Mutate(c=>c.FeatureResults.Add(new(){Id=l.Id,AtUtc=LocalState.Stamp(Now),State=state,Detail=state=="failed"?"Required input or native chat guard unavailable.":""}))){reportedLock=l.Id;reportedLockState=state;nextSync=0;}}
    internal void VerifyConnection(){if(Configuration.DeviceToken.Length==0||credentialTask is not null)return;frameSteps.RetryNow();nextSync=syncRetryUntil=0;credentialToken=Configuration.DeviceToken;credentialTask=service.Verify(credentialToken,cancel.Token);}
    private void TrackCharacter(){
        var id=CurrentCharacterId;
        if(id.Length>0 && Configuration.Characters.FirstOrDefault(c=>c.Id==id) is { } existing && (existing.Name!=Player.CharacterName || existing.HomeWorld!=Player.HomeWorld.Value.Name.ToString()))Mutate(c=>{var x=c.Characters.Single(c=>c.Id==id);x.Name=Player.CharacterName;x.HomeWorld=Player.HomeWorld.Value.Name.ToString();});
        if(id.Length>0 && !Configuration.Characters.Any(c=>c.Id==id))Mutate(c=>c.Characters.Add(new(){Id=id,Name=Player.CharacterName,HomeWorld=Player.HomeWorld.Value.Name.ToString(),Allowed=false}));
        var marker=id+":"+CharacterAllowed;
        if(marker==observedCharacter)return;
        input.Release();
        ReleaseCleanup("Sleep",()=>sleep.End("Character changed."));
        ReleaseCleanup("activity",()=>recorder.Stop("character changed"));
        ReleaseCleanup("travel",travel.Reset);
        if(observedCharacter.Length>0)ReleaseCleanup("integrations",integrations.CharacterChanged);
        observedCharacter=marker;characterRevision++;AdvanceAccessRevision();masterInput.Invalidate();sessionId="";loginAt="";loginSource="";localRelease=false;pendingInventory=null;inventoryDirty=true;observationUpload.Reset();nextSync=0;
        if(SharingAllowed)StartSession("first-observed");
    }
    private bool UpdateStep(string name,System.Action update)=>frameSteps.Run(name,update,Environment.TickCount64,
        (step,error)=>Log.Warning(error,"Pet Service update failed: {Step}; retrying.",step));
    private bool ConnectionStep(string name,System.Action update)
    {
        var ready=UpdateStep(name,update);
        if(!ready) {
            input.Release();
            if(IsPaired && !KillSwitchOn)ServiceStatus="Synchronization is waiting for "+name+". Retrying automatically; see /xllog for details.";
        }
        return ready;
    }
    private void Update(IFramework framework)
    {
        if(disposed)return;
        // Connection completion and startup have priority over optional features.
        // A fault in an integration, sound, chat, or recorder cannot skip them.
        var pairingWasPending=pairTask is not null;
        if(ConnectionStep("character access",UpdateCharacter) && ConnectionStep("connection replies",CompleteNetwork)) {
            // Pairing can approve the current character. Advance that barrier
            // before starting a request, rather than canceling it next frame.
            if(!pairingWasPending || pairTask is not null || ConnectionStep("character access",TrackCharacter))
                ConnectionStep("connection request",StartSynchronization);
        }
        UpdateStep("safety notices",UploadSafetyNotices);
        UpdateStep("integrations",integrations.Update);
        UpdateStep("chat input",chatTwo.Update);
        UpdateStep("local interactions",V6.Update);
        UpdateStep("Sleep",()=>sleep.Update(GuardAvailable && sleepInput.Available));
        UpdateStep("sounds",Sounds.Update);
        UpdateStep("private chat",()=>{if(!PrivateChat.IsOpen)PrivateChat.Session.Tick(false);});
        UpdateStep("inventory",()=>{
            if(SharingAllowed && Ready && inventoryDirty && Environment.TickCount64>=inventoryAfter) {
                try{pendingInventory=InventoryReader.Capture(this);inventoryDirty=false;}
                catch{inventoryAfter=Environment.TickCount64+15000;throw;}
            }
        });
        UpdateStep("gameplay state",UpdateLockResult);
        UpdateStep("activity",()=>recorder.Update(Observe));
        UpdateStep("travel",()=>travel.Update(Observe));
        UpdateStep("update notices",UpdateNotices);
        UpdateStep("reminder schedules",RefreshSchedules);
        if(!UpdateStep("input guard",()=>{if(GuardRequested)input.SuppressKeyboardOnFrameworkUpdate();else input.Release();}))
            input.Release();
    }
    private void UpdateCharacter()
    {
        var logged=ClientState.IsLoggedIn;
        if(logged && !previousLogin && SharingAllowed)StartSession("first-observed");
        if(!logged && previousLogin){localRelease=false;sessionId="";loginAt="";loginSource="";nextSync=0;input.Release();}
        previousLogin=logged;
        TrackCharacter();
    }
    private void UpdateNotices()
    {
        updates.Update();
        if(Ready && !changesShown) {
            if(changesEligible==0)changesEligible=Environment.TickCount64+3000;
            if(Environment.TickCount64>=changesEligible){changesShown=true;if(Configuration.LastAcknowledgedVersion!="0.6.0.3")changes.IsOpen=true;}
        } else if(!Ready)changesEligible=0;
    }
    private void UploadSafetyNotices()
    {
        // Only explicit safety notices use this route. It carries no observation
        // and remains usable while the kill switch pauses normal synchronization.
        if(IsPaired && safetyTask is null && (Configuration.SafetyOutbox.Count>0 || KillSwitchOn&&Configuration.FeatureResults.Any(r=>r.State is "released" or "failed" or "unavailable")) && Environment.TickCount64>=nextSafetySync) {
            nextSafetySync=Environment.TickCount64+15000;
            var batch=JsonSerializer.Deserialize<List<Choice>>(JsonSerializer.Serialize(Configuration.SafetyOutbox.Take(20),ServiceClient.Json),ServiceClient.Json)!;
            submittedSafetyResults=Configuration.FeatureResults.Where(r=>r.State is "released" or "failed" or "unavailable").Take(100).ToList();
            safetyTask=service.DeviceEvents(Configuration.DeviceToken,batch,cancel.Token,submittedSafetyResults);
    }
    }
    private void RefreshSchedules()
    {
        if(Ready && SharingAllowed && Environment.TickCount64>=nextEvaluation) {
            nextEvaluation=Environment.TickCount64+1000;
            // Compute on a copy, then persist the whole change atomically.
            var prompts=JsonSerializer.Deserialize<List<Prompt>>(JsonSerializer.Serialize(Configuration.Prompts,ServiceClient.Json),ServiceClient.Json)!;
            if(LocalState.Refresh(Configuration.Reminders,prompts,Now) && !Mutate(c=>c.Prompts=prompts))Configuration.Prompts=prompts;
            ScheduleError="";
    }
    }
    private void StartSynchronization()
    {
        var syncTick=Environment.TickCount64;
        if(masterInput.Enabled && IsPaired && !NetworkBusy && syncTick>=syncRetryUntil
            && syncTick>=lastSyncStarted+1000 && (syncTick>=nextSync || activityUploadRequested)) {
            nextSync=syncTick+5000;lastSyncStarted=syncTick;
            var batch=JsonSerializer.Deserialize<List<Choice>>(JsonSerializer.Serialize((SharingAllowed?Configuration.Outbox.Take(20):[]),ServiceClient.Json),ServiceClient.Json)!;
            var flush=activityUploadRequested || syncTick>=nextActivityFlush || Configuration.ActivityOutbox.Count>=50;
            var activity=flush && SharingAllowed?Configuration.ActivityOutbox.Take(50).ToList():[];
            submittedActivity=activity.Count>0;
            if(flush){nextActivityFlush=syncTick+60000;activityUploadRequested=false;}
            Observation? current=null;
            if(SharingAllowed)UpdateStep("game status",()=>current=Observe());
            var reportQueueSaved=freshReportRequestId is null || UpdateStep("report queue",()=>{if(!recorder.FlushForReport())throw new IOException("Saved activity is waiting for local persistence.");});
            if(SharingAllowed && freshReportRequestId is not null){activity=Configuration.ActivityOutbox.Take(50).ToList();submittedActivity=activity.Count>0;}
            submittedObservation=SharingAllowed && current is not null && observationUpload.NeedsSnapshot(current,syncTick,activity.Any(a=>a.Kind is not "interval" and not "position"),batch.Count>0,liveReportsRequested,freshReportRequestId is not null)?current:null;
            submittedReportRequestId=SharingAllowed && freshReportRequestId is not null && reportQueueSaved && Configuration.ActivityOutbox.Count<=50 && Configuration.Outbox.Count<=20?freshReportRequestId:null;
            syncRevision=masterInput.Revision;syncCharacterRevision=characterRevision;submittedInventoryAt=pendingInventory?.CapturedAtUtc??"";submittedCatalog=integrations.Catalog;
            submittedResults=SharingAllowed?JsonSerializer.Deserialize<List<FeatureResult>>(JsonSerializer.Serialize(Configuration.FeatureResults.Take(100),ServiceClient.Json),ServiceClient.Json)!:[];
            if(Configuration.LastSyncAtUtc is null) {
                ServiceStatus="Paired. Connecting to service…";
                Log.Information("Pet Service initial synchronization request started; game status included: {Included}.",submittedObservation is not null);
            }
            syncTask=service.Sync(Configuration.DeviceToken,submittedObservation,batch,activity,submittedReportRequestId,
                new{revision=Configuration.AccessRevision,state=KillSwitchOn?"paused":!ClientState.IsLoggedIn?"logged-out":CurrentCharacterId.Length==0?"waiting":CharacterAllowed?"allowed":"unapproved",characterId=CharacterAllowed?CurrentCharacterId:"",characters=Configuration.Characters.Where(c=>c.Allowed).Select(c=>new{id=c.Id,name=c.Name,homeWorld=c.HomeWorld}).ToList()},
                SharingAllowed?pendingInventory:null,SharingAllowed?integrations.Catalog:null,submittedResults,SharingAllowed?Configuration.TaskEvents.Take(100).ToList():[],masterInput.RequestCancellation,SharingAllowed?integrations.Icons:null,SharingAllowed?Configuration.V6Outbox.Take(50).ToList():[]);
    }
    }
    private void CompleteNetwork()
    {
        if(credentialTask is {IsCompleted:true}){var task=credentialTask;credentialTask=null;try{var result=task.GetAwaiter().GetResult();if(result.Active && result.PetId==Configuration.PetId && credentialToken==Configuration.DeviceToken){Mutate(c=>{c.Revoked=false;if(result.AccessRevision>c.AccessRevision)c.AccessRevision=checked(result.AccessRevision+1);});ServiceStatus="Credential confirmed; reconnecting…";nextSync=0;}}catch(ServiceFailure e){if(e.Revoked && credentialToken==Configuration.DeviceToken)Mutate(c=>c.RevocationConfirmed=true);}catch{ServiceStatus="Could not verify saved authorization. Use Verify connection in Setup.";}}
        if(pairTask is {IsCompleted:true}) {
            var task=pairTask;pairTask=null;var target=pairingCharacter;pairingCharacter=null;
            try {
                var result=task.GetAwaiter().GetResult();
                if(target is null || !Guid.TryParse(result.PetId,out _) || result.Token.Length!=43)throw new ServiceFailure("Invalid pairing response.");
                if(Mutate(c=>{
                    var same=c.PetId==result.PetId;
                    if(!same){c.V6Outbox.Clear();c.V6ChatIds.Clear();c.PointNoticeIds.Clear();c.RedemptionNoticeIds.Clear();c.DisplayedChatIds.Clear();c.ActivityOutbox.Clear();c.Outbox.Clear();c.SafetyOutbox.Clear();c.FeatureResults.Clear();c.TaskEvents.Clear();c.Features=new();c.Dynamic=new();c.Reminders.Clear();c.Prompts.Clear();c.Characters.ForEach(x=>x.Allowed=false);}
                    target.Approve(c.Characters,!same);c.AccessRevision=checked(Math.Max(0,c.AccessRevision)+1);
                    c.QueuesPetId=result.PetId;c.RevocationConfirmed=false;c.ServiceUrl=ServiceClient.BaseUrl;c.DeviceToken=result.Token;c.PetId=result.PetId;c.PetName=result.PetName;c.Revoked=false;c.LastSyncAtUtc=null;c.ClockOffsetMilliseconds=0;
                }))
                {recorder.ClearPairingState();observationUpload.Reset();if(SharingAllowed)StartSession("first-observed");ServiceStatus=KillSwitchOn ? $"Paired. Kill switch remains on; {MasterName} input and status sharing are paused." : "Paired. Synchronizing…";nextSync=0;syncRetryUntil=0;}
                else ServiceStatus="Pairing succeeded remotely, but the credential could not be saved. Fix the save problem and request a new !pair code.";
            } catch {ServiceStatus=$"Pairing failed. Check the code and service availability. Request a fresh pairing code from {MasterName} if necessary.";}
        }
        if(safetyTask is {IsCompleted:true}) {
            var task=safetyTask;safetyTask=null;
            try {
                var result=task.GetAwaiter().GetResult();
                if(result.PetId!=Configuration.PetId)throw new ServiceFailure("Profile mismatch.");
                Mutate(c=>{c.SafetyOutbox.RemoveAll(e=>result.AcceptedEventIds.Contains(e.Id));QuarantineResults(c,result.RejectedResults);c.FeatureResults.RemoveAll(e=>result.AcceptedResultIds.Contains(e.Id)&&submittedSafetyResults.Any(sent=>sent.Id==e.Id&&sent.State==e.State&&sent.AtUtc==e.AtUtc));});
            } catch(ServiceFailure e) {
                if(e.Revoked)Mutate(c=>{c.Revoked=true;c.RevocationConfirmed=true;c.SafetyOutbox.Clear();});
                nextSafetySync=Environment.TickCount64+15000;
            } catch {nextSafetySync=Environment.TickCount64+15000;}
        }
        if(syncTask is {IsCompleted:true}) {
            var task=syncTask;syncTask=null;
            try {
                var result=task.GetAwaiter().GetResult();
                if(!masterInput.Accepts(syncRevision) || syncCharacterRevision!=characterRevision)return;
                if(result.PetId!=Configuration.PetId)throw new ServiceFailure("Profile mismatch.");
                var serverTime=LocalState.Parse(result.ServerTimeUtc);
                var firstSynchronization=Configuration.LastSyncAtUtc is null;
                if(Mutate(c=>{
                    foreach(var r in result.RejectedActivity.Where(r=>!r.Retryable)){var e=c.ActivityOutbox.FirstOrDefault(e=>e.Id==r.Id);if(e is not null){c.Recovery.Add(new(){Id=e.Id,Kind="activity",Code=r.Code,Payload=JsonSerializer.Serialize(e,ServiceClient.Json),PetId=c.PetId});c.ActivityOutbox.Remove(e);}}
                    foreach(var r in result.RejectedEvents.Where(r=>!r.Retryable)){var e=c.Outbox.FirstOrDefault(e=>e.Id==r.Id);if(e is not null){c.Recovery.Add(new(){Id=e.Id,Kind="reply",Code=r.Code,Payload=JsonSerializer.Serialize(e,ServiceClient.Json),PetId=c.PetId});c.Outbox.Remove(e);}}
                    QuarantineResults(c,result.Features.RejectedResults);
                    foreach(var r in result.Features.RejectedTaskEvents){var e=c.TaskEvents.FirstOrDefault(e=>e.Id==r.Id);if(e is not null){c.Recovery.Add(new(){Id=e.Id,Kind="task",Code=r.Code,Payload=JsonSerializer.Serialize(e,ServiceClient.Json),PetId=c.PetId});c.TaskEvents.Remove(e);}}
                    ArchiveRecovery(c);
                    if(result.Features.Allowed){
                        foreach(var rejected in result.Features.RejectedV6Events){var item=c.V6Outbox.FirstOrDefault(e=>e.Id==rejected.Id);if(item is not null){c.Recovery.Add(new(){Id=item.Id,Kind="interaction",Code=rejected.Code,Payload=JsonSerializer.Serialize(item,ServiceClient.Json),PetId=c.PetId});c.V6Outbox.Remove(item);}}
                        c.V6Outbox.RemoveAll(e=>result.Features.AcceptedV6EventIds.Contains(e.Id));
                        V6Local.Merge(result.Features.V6,c.Features.V6,c.V6Outbox,serverTime);c.Features=result.Features;
                    }else{c.Features.Presence=result.Features.Presence;}c.FeatureResults.RemoveAll(e=>result.Features.AcceptedResultIds.Contains(e.Id)&&submittedResults.Any(sent=>sent.Id==e.Id&&sent.State==e.State&&sent.AtUtc==e.AtUtc&&sent.Detail==e.Detail));c.TaskEvents.RemoveAll(e=>result.Features.AcceptedTaskEventIds.Contains(e.Id));
                    c.ActivityOutbox.RemoveAll(e=>result.AcceptedActivityIds.Contains(e.Id));result.Settings.NormalizeContacts();c.Dynamic=result.Settings;
                    c.Outbox.RemoveAll(e=>result.AcceptedEventIds.Contains(e.Id));
                    if(result.Features.Allowed){c.Prompts=LocalState.Merge(c.Prompts,result,c.Outbox);c.Reminders=result.Reminders;}
                    c.LastSyncAtUtc=result.ServerTimeUtc;c.ClockOffsetMilliseconds=(serverTime-DateTimeOffset.UtcNow).TotalMilliseconds;
                })){
                    if(submittedObservation is not null)observationUpload.Accepted(submittedObservation,Environment.TickCount64);
                    submittedObservation=null;syncRetryUntil=0;if(pendingInventory?.CapturedAtUtc==submittedInventoryAt)pendingInventory=null;integrations.AcceptedCatalog(submittedCatalog);integrations.AcceptedIcons(result.Features.AcceptedIconIds);integrations.RejectedIcons(result.Features.RejectedIconIds);
                    ServiceStatus=CharacterAllowed?(frameSteps.FailedSteps.Contains("game status")?"Connected. Game status is temporarily unavailable and will retry.":"Connected."):$"This character is not enabled. {MasterName} controls are paused.";
                    if(firstSynchronization)Log.Information("Pet Service initial synchronization response applied.");
                    if(result.Features.InventoryRequest is {ValueKind:JsonValueKind.Object} request && request.TryGetProperty("id",out var rid) && rid.GetString()!=inventoryRequestId){inventoryRequestId=rid.GetString()??"";MarkInventoryDirty();}
                    DeliverChat(result.ChatMessages);V6.NotifyChanges();
                    var liveStarted=!liveReportsRequested && result.LiveReportsRequested;
                    liveReportsRequested=result.LiveReportsRequested;
                    freshReportRequestId=result.FreshReportRequestId;
                    if(freshReportRequestId is not null){integrations.RequestCatalog();recorder.FlushForReport();activityUploadRequested=true;nextSync=0;}
                    else if(liveStarted)nextSync=0;
                    if(submittedActivity && Configuration.ActivityOutbox.Count>0)activityUploadRequested=true;
                    if(Configuration.ActivityOutbox.Count>=50)nextSync=Environment.TickCount64+1000;
                } else {
                    ServiceStatus="Service replied, but synchronization could not be saved locally. Retrying.";
                    syncRetryUntil=nextSync=Environment.TickCount64+15000;
                }
            } catch(ServiceFailure e) {
                if(!masterInput.Accepts(syncRevision) || syncCharacterRevision!=characterRevision)return;
                observationUpload.Reset();
                if(e.Revoked){Mutate(c=>{c.Revoked=true;c.RevocationConfirmed=true;});integrations.Release();input.Release();ServiceStatus="Device authorization was revoked. Request a new pairing code.";}
                else {if(e.Message=="STALE_DEVICE_STATE")VerifyConnection();ServiceStatus=$"Synchronization failed ({e.ConnectionDetail}). Cached reminders and saved choices remain local.";Log.Warning("Pet Service synchronization failed: {Detail}",e.ConnectionDetail);syncRetryUntil=nextSync=Environment.TickCount64+15000;activityUploadRequested=true;}
            } catch(Exception error) {
                if(!masterInput.Accepts(syncRevision) || syncCharacterRevision!=characterRevision)return;
                observationUpload.Reset();
                Log.Warning(error,"Pet Service could not complete synchronization; retrying.");
                ServiceStatus="Could not synchronize. Cached reminders and saved choices remain local.";syncRetryUntil=nextSync=Environment.TickCount64+15000;activityUploadRequested=true;
            }
        }
    }
    private static void QuarantineResults(Configuration c,List<UploadRejection> rejected){foreach(var r in rejected.Where(x=>!x.Retryable)){foreach(var e in c.FeatureResults.Where(x=>x.Id==r.Id&&(r.State is null||x.State==r.State)).ToList()){c.Recovery.Add(new(){Id=e.Id,Kind="control-result",Code=r.Code,Payload=JsonSerializer.Serialize(e,ServiceClient.Json),PetId=c.PetId});c.FeatureResults.Remove(e);}}ArchiveRecovery(c);}
    private void DeliverChat(List<MasterChatMessage> messages)
    {
        if(!SharingAllowed || !Ready)return;
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
    {if(disposed)return;try {PrivateChat.Session.Focused=!Dalamud.Bindings.ImGui.ImGui.GetIO().AppFocusLost;windows.Draw();if(GuardRequested && !HasVisibleReminder){var state=ChatState;var mouse=Dalamud.Bindings.ImGui.ImGui.GetIO().MousePos-Dalamud.Bindings.ImGui.ImGui.GetMainViewport().Pos;input.CaptureRenderedFrame(settings.EditingText || PrivateChat.IsOpen&&PrivateChat.EditingText,state.Available&&(state.InputArea.Contains(mouse)||state.LogArea.Contains(mouse)));}}catch{input.Release();}}
    internal void CaptureInput(bool editing,bool mouseOverChat){if(GuardRequested)input.CaptureRenderedFrame(editing || settings.EditingText || PrivateChat.IsOpen&&PrivateChat.EditingText,mouseOverChat);else input.Release();}
    internal void FocusGameChat(){settings.ClearTextFocus();nativeChat.FocusInput();}
    internal void ReleaseInput()=>input.Release();
    public void Dispose()
    {
        if (!disposed)
        {
            // Stop callbacks before any resource is disposed. Each registered
            // cleanup step is attempted even if another component throws.
            disposed = true;
            shutdown.Add("UI draw handler", () => PluginInterface.UiBuilder.Draw -= Draw, true);
            shutdown.Add("UI config handler", () => PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings, true);
            shutdown.Add("UI main handler", () => PluginInterface.UiBuilder.OpenMainUi -= OpenSettings, true);
            shutdown.Add("login handler", () => ClientState.Login -= OnLogin, true);
            shutdown.Add("framework handler", () => Framework.Update -= Update, true);
            shutdown.Add("petservice command", () => Commands.RemoveHandler("/petservice"), true);
            shutdown.Add("toh command", () => Commands.RemoveHandler("/toh"), true);
            shutdown.Add("gameplay input capture", input.Release);
            shutdown.Add("Sleep release", () => sleep.End("Plugin unloaded."));
            shutdown.Add("pending service requests", cancel.Cancel);
            shutdown.Add("master input requests", masterInput.Dispose);
            shutdown.Add("window registrations", windows.RemoveAllWindows, true);
            shutdown.Add("Sleep input hook", sleepInput.Dispose, true);
            shutdown.Add("chat command hook", chatCommands.Dispose, true);
            shutdown.Add("movement hook", movement.Dispose, true);
            shutdown.Add("travel hooks", travel.Dispose, true);
            shutdown.Add("activity hooks and handlers", recorder.Dispose, true);
            shutdown.Add("managed integration effects", integrations.Dispose);
            shutdown.Add("private chat window", PrivateChat.Dispose);
            shutdown.Add("Master sound window", MasterSounds.Dispose);
            shutdown.Add("remote sounds", Sounds.Dispose);
            shutdown.Add("settings window", settings.Dispose);
            shutdown.Add("service HTTP client", service.Dispose);
            shutdown.Add("service cancellation source", cancel.Dispose);
        }
        shutdown.Run((step, error) => Log.Error(error, "Pet Service cleanup failed: {Step}", step));
        // A final logout heartbeat cannot be guaranteed during unload/crash. The
        // service marks observations stale after 45 seconds instead of assuming logout.
    }
}
