using System.Text.Json;

namespace PetService.Core;

internal sealed class MasterPortalSession(MasterPortalClient client) : IDisposable
{
    private string password="";
    private CancellationTokenSource cancel=new();
    private Task<AdminDashboard>? dashboardTask;
    private Task<SettingsSnapshot>? settingsTask;
    private string settingsTaskPet="";
    private long nextSettings,settingsRevision,settingsTaskRevision;
    private bool settingsVisible;private string settingsSection="controls",settingsCharacter="";
    internal long JournalRevision=>selectionRevision;
    internal long SettingsReadCount {get;private set;}
    private Task<ControlHistoryPage>? controlHistoryTask;
    internal List<ControlEvent> OlderControlHistory{get;private set;}=[];
    internal string? ControlCursor{get;private set;}
    internal void LoadOlderControls(){if(!Unlocked||Busy||Dashboard?.Selected is not { } pet)return;var cursor=OlderControlHistory.Count==0?pet.Features.ControlHistoryCursor:ControlCursor;if(string.IsNullOrEmpty(cursor))return;controlHistoryTask=client.ControlHistory(password,Selected,cursor,cancel.Token);}
    private Task<HistoryPage>? historyTask;
    private bool appendHistory;
    internal List<ActivityRecord> History {get;private set;}=[];
    internal string HistoryCursor {get;private set;}="";
    internal string HistoryKind {get;private set;}="";
    internal void LoadHistory(string kind,string from,string to,string search,bool append=false,string method="") {
        if(!Unlocked || Busy)return;
        appendHistory=append;HistoryKind=kind;
        historyTask=client.History(password,Selected,kind,from,to,search,append?HistoryCursor:"",cancel.Token,method);
    }
    private Task<AdminActionResult>? actionTask;
    private AdminAction? activeAction,retryAction;
    private readonly string viewerId=Guid.NewGuid().ToString();
    private Task<LiveReport>? liveTask;
    private string liveTaskPet="",leasedPet="";
    private long selectionRevision,liveTaskSelection;
    private bool liveTaskWatched;
    private string? requestedReportAt;
    private bool refreshAfterReport;
    private long nextLive;
    private double clockOffset;
    internal AdminDashboard? Dashboard { get; private set; }
    internal string Selected { get; private set; }="";
    internal string Error { get; private set; }="";
    internal string Notice { get; private set; }="";
    internal bool Unlocked=>password.Length>0 && Dashboard is not null;
    internal bool Busy=>dashboardTask is not null || actionTask is not null || historyTask is not null || controlHistoryTask is not null;
    internal bool CanRetry=>retryAction is not null && Unlocked && !Busy;
    internal bool AutoRefresh { get; set; }=true;
    internal DateTimeOffset Now=>DateTimeOffset.UtcNow.AddMilliseconds(clockOffset);
    internal AdminAction? CompletedAction { get; private set; }
    internal AdminActionResult? CompletedResult { get; private set; }
    internal void Unlock(string value)
    {
        if(Busy || Unlocked || value.Length==0)return;
        password=value;Error="";Refresh();
    }
    internal Task<JsonElement> ReadJournal(string query,bool analytics)=>client.Journal(password,Selected,query,analytics,cancel.Token);
    internal Task<byte[]> Portrait()=>client.Portrait(password,cancel.Token);
    internal void Refresh()
    {
        if(Busy || password.Length==0)return;
        settingsRevision++;nextSettings=0;
        dashboardTask=client.Dashboard(password,Selected,cancel.Token);
    }
    internal void Select(string pet)
    {
        if(Busy || !Unlocked || !MasterPortalPolicy.ValidName(pet))return;
        Hide();settingsRevision++;selectionRevision++;requestedReportAt=null;OlderControlHistory=[];ControlCursor=null;History=[];HistoryCursor="";HistoryKind="";Selected=pet;Refresh();
    }
    private static string Fingerprint(AdminAction action)=>JsonSerializer.Serialize(action with{RequestId=""},ServiceClient.Json);
    internal void Action(AdminAction value)
    {
        if(!Unlocked || Busy)return;
        settingsRevision++;
        var copy=JsonSerializer.Deserialize<AdminAction>(JsonSerializer.Serialize(value,ServiceClient.Json),ServiceClient.Json)!;
        activeAction=retryAction is not null && Fingerprint(retryAction)==Fingerprint(copy) ? retryAction : copy;
        retryAction=activeAction;Error="";Notice="";
        actionTask=client.Action(password,activeAction,cancel.Token);
    }
    internal void Retry(){if(CanRetry)Action(retryAction!);}
    internal void Update()
    {
        if(settingsTask is {IsCompleted:true}) {
            var task=settingsTask;settingsTask=null;
            try {
                var result=task.GetAwaiter().GetResult();
                if(settingsTaskRevision==settingsRevision && result.Pet==settingsTaskPet && Selected==settingsTaskPet && Dashboard?.Selected is { } pet && pet.Name==Selected) {
                    if(!DateTimeOffset.TryParse(result.ServerTimeUtc,out var stamp))throw new AdminFailure("INVALID_RESPONSE");
                    result.Settings.NormalizeContacts();pet.Settings=result.Settings;if(result.Identity is not null)Dashboard.Identity=result.Identity;if(result.Controls is not null){pet.Features.Presence=result.Controls.Presence;switch(result.Section){case "inventory":pet.Features.SnapshotIssues=result.Controls.SnapshotIssues;pet.Features.Inventory=result.Controls.Inventory;break;case "tasks":pet.Features.Tasks=result.Controls.Tasks;break;case "rewards":pet.Features.Rewards=result.Controls.Rewards;break;default:pet.Features.Characters=result.Controls.Characters;pet.Features.SnapshotIssues=result.Controls.SnapshotIssues;pet.Features.Lock=result.Controls.Lock;pet.Features.Sleep=result.Controls.Sleep;pet.Features.Catalog=result.Controls.Catalog;pet.Features.Catalogs=result.Controls.Catalogs;pet.Features.IntegrationCommands=result.Controls.IntegrationCommands;if(result.Controls.ActiveIntegrationCommands is not null)pet.Features.ActiveIntegrationCommands=result.Controls.ActiveIntegrationCommands;pet.Features.ControlHistory=result.Controls.ControlHistory;pet.Features.ControlHistoryCursor=result.Controls.ControlHistoryCursor;break;}}SettingsReadCount++;Error="";
                    clockOffset=(stamp-DateTimeOffset.UtcNow).TotalMilliseconds;
                }
            } catch(Exception e){if(settingsTaskRevision==settingsRevision){Failure(e);nextSettings=Environment.TickCount64+15000;}}
        }
        if(liveTask is {IsCompleted:true}) {
            var task=liveTask;liveTask=null;
            try {
                var result=task.GetAwaiter().GetResult();
                if(liveTaskWatched && leasedPet!=liveTaskPet && password.Length>0)ObserveFailure(client.Live(password,liveTaskPet,viewerId,"stop",CancellationToken.None));
                if(liveTaskSelection==selectionRevision && Dashboard?.Selected is { } pet && liveTaskPet==Selected && pet.Name==Selected) {
                    pet.Status=result.Status;pet.Report=result.Report;if(result.Presence is not null)pet.Features.Presence=result.Presence;if(result.Lock is not null)pet.Features.Lock=result.Lock;
                    if(result.ChatMessages is not null)pet.ChatMessages=result.ChatMessages;
                    var profile=Dashboard.Profiles.FirstOrDefault(p=>p.Name==Selected);if(profile is not null)profile.Status=result.Status;
                    if(DateTimeOffset.TryParse(result.ServerTimeUtc,out var stamp))clockOffset=(stamp-DateTimeOffset.UtcNow).TotalMilliseconds;
                    CheckReport();
                }
            } catch(Exception e){if(liveTaskSelection==selectionRevision){Failure(e);nextLive=Environment.TickCount64+15000;}}
        }
        if(controlHistoryTask is {IsCompleted:true}){var task=controlHistoryTask;controlHistoryTask=null;try{var result=task.GetAwaiter().GetResult();OlderControlHistory.AddRange(result.Records);ControlCursor=result.NextCursor;Error="";}catch(Exception e){Failure(e);}}
        if(historyTask is {IsCompleted:true}) {
            var task=historyTask;historyTask=null;
            try {var result=task.GetAwaiter().GetResult();History=appendHistory?[..History,..result.Records]:result.Records;HistoryCursor=result.NextCursor??"";Error="";}
            catch(Exception e){Failure(e);}
        }
        if(dashboardTask is {IsCompleted:true}) {
            var task=dashboardTask;dashboardTask=null;
            try {
                var result=task.GetAwaiter().GetResult();
                if(!DateTimeOffset.TryParse(result.ServerTimeUtc,out var stamp))throw new AdminFailure("INVALID_RESPONSE");
                Dashboard=result;Selected=result.Selected?.Name ?? "";clockOffset=(stamp-DateTimeOffset.UtcNow).TotalMilliseconds;Error="";
            } catch(Exception exception){Failure(exception);}
        }
        if(actionTask is {IsCompleted:true}) {
            var task=actionTask;actionTask=null;
            try {
                CompletedResult=task.GetAwaiter().GetResult();CompletedAction=activeAction;retryAction=null;
                if(activeAction?.Action=="saveSettings" && Dashboard?.Selected is { } savedPet && savedPet.Name==activeAction.Pet) {
                    savedPet.Settings=DynamicSettingsEditor.Clone(CompletedResult.Settings ?? activeAction.Settings!);
                    nextSettings=0;
                }
                Notice=activeAction?.Action switch{"sendChat"=>"Master chat message queued.","cancelChat"=>"Chat cancellation saved.","requestReport"=>"Fresh report requested. Waiting for the pet's plugin…","sendMessage"=>"Message queued.","createPet"=>"Pet profile created.","pair"=>"Pairing code generated.","revoke"=>"Device revoked.","addReminder"=>"Daily reminder added.","disableReminder"=>"Reminder disabled.","cancelPrompt"=>"Pending prompt canceled.","taskEvidence"=>"Task evidence saved.","honorific" or "moodles"=>"Request queued. Waiting for the pet’s plugin.",_=>"Saved."};
                if(activeAction?.Action=="requestReport"){requestedReportAt=CompletedResult.RequestedAtUtc;nextLive=0;}
                if(activeAction?.Action=="createPet")Selected=CompletedResult.Pet;
                if(activeAction?.Action=="deletePet")Selected="";
                Refresh();
            } catch(Exception exception){Failure(exception);}
            activeAction=null;
        }
        CheckReport();
        if(refreshAfterReport && !Busy){refreshAfterReport=false;Refresh();}
    }
    private void CheckReport()
    {
        if(requestedReportAt is null)return;
        if(DateTimeOffset.TryParse(requestedReportAt,out var requested) && DateTimeOffset.TryParse(Dashboard?.Selected?.Report?.CompletedAtUtc,out var completed) && completed>=requested) {
            requestedReportAt=null;refreshAfterReport=true;Notice="Fresh report received; queued observations uploaded.";
        } else if(DateTimeOffset.TryParse(requestedReportAt,out requested) && (Now-requested).TotalSeconds>=45) {
            requestedReportAt=null;Notice="Report request remains queued. The pet's plugin may be offline or paused.";
        }
    }
    internal void TickVisible(bool overview,bool chats=false,bool settings=false,string section="controls",string characterId="")
    {
        var live=overview && AutoRefresh;
        if(!live)StopLive();
        if(settings && (!settingsVisible||settingsSection!=section||settingsCharacter!=characterId)){nextSettings=0;settingsRevision++;}
        settingsVisible=settings;settingsSection=section;settingsCharacter=characterId;
        if(settings && Unlocked && Selected.Length>0 && !Busy && settingsTask is null && Environment.TickCount64>=nextSettings) {
            settingsTaskPet=Selected;settingsTaskRevision=settingsRevision;nextSettings=Environment.TickCount64+5000;
            settingsTask=client.Settings(password,Selected,cancel.Token,settingsSection,settingsCharacter);
        }
        if(!Unlocked || Selected.Length==0 || liveTask is not null || Busy || (!live && !chats && requestedReportAt is null) || Environment.TickCount64<nextLive)return;
        liveTaskPet=Selected;liveTaskSelection=selectionRevision;liveTaskWatched=live;nextLive=Environment.TickCount64+5000;
        if(live)leasedPet=Selected;
        liveTask=client.Live(password,Selected,viewerId,live?"watch":chats?"chats":"read",cancel.Token);
    }
    internal void Hide()
    {
        settingsVisible=false;nextSettings=0;StopLive();
    }
    internal void RefreshSettings(){settingsRevision++;nextSettings=0;}
    private void StopLive()
    {
        if(leasedPet.Length==0)return;
        var pet=leasedPet;leasedPet="";
        if(password.Length>0)ObserveFailure(client.Live(password,pet,viewerId,"stop",CancellationToken.None));
    }
    internal void ClearCompletion(){CompletedAction=null;CompletedResult=null;}
    private void Failure(Exception exception)
    {
        if(exception is AdminFailure{Unauthorized:true}){Lock();Error="Administrator password was not accepted. Unlock again.";return;}
        Error=exception.Message switch {
            "TASK_EVIDENCE_LIMIT"=>"This task already has 100 attached observations.","OBSERVATION_NOT_FOUND"=>"This observation is no longer available. Search again.","INVALID_OBSERVATION"=>"Choose an event observation to attach to this task.","CHARACTER_NOT_ALLOWED"=>"This character is not approved for Master controls.","CHAT_QUEUE_FULL"=>"This pet already has 100 queued chat messages.","INVALID_CHAT_EXPIRY"=>"Choose 0–10,080 minutes for message expiration.","PET_NOT_PAIRED"=>"Pair this pet before requesting a fresh report.","PET_NAME_EXISTS"=>"A pet with that name already exists.","INVALID_PET_NAME"=>"Use a lowercase name with letters, numbers, hyphens or underscores.",
            "PET_NOT_FOUND"=>"This pet could not be found. Lock and reopen the portal.","PET_TIME_ZONE_UNKNOWN"=>"Connect this pet's plugin once before adding a daily reminder.",
            "INVALID_REPLY_CHOICES"=>"Check the button labels: up to 32 different labels, 80 characters each.","INVALID_REPLY_OPTIONS"=>"Add a reply button or allow a written reply.",
            "REMINDER_LIMIT"=>"This pet already has 32 enabled reminders.","MESSAGE_QUEUE_FULL"=>"This pet already has 50 pending messages.","REQUEST_ID_CONFLICT"=>"The action identifier was already used with different details. Refresh and try again.",
            _=>"Could not complete the request. Refresh or retry; the service may be temporarily unavailable."
        };
        
    }
    internal void Lock()
    {
        Hide();selectionRevision++;requestedReportAt=null;refreshAfterReport=false;
        cancel.Cancel();cancel.Dispose();cancel=new();
        ObserveFailure(controlHistoryTask);controlHistoryTask=null;OlderControlHistory=[];ControlCursor=null;
        ObserveFailure(historyTask);historyTask=null;History=[];HistoryCursor="";HistoryKind="";
        ObserveFailure(dashboardTask);ObserveFailure(actionTask);dashboardTask=null;actionTask=null;
        ObserveFailure(liveTask);liveTask=null;
        ObserveFailure(settingsTask);settingsTask=null;settingsRevision++;settingsVisible=false;nextSettings=0;
        password="";Dashboard=null;Selected="";activeAction=null;retryAction=null;CompletedAction=null;CompletedResult=null;
        Error="";Notice="";clockOffset=0;
    }
    private static void ObserveFailure(Task? task)
    {
        if(task is not null)_=task.ContinueWith(t=>{_=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted,TaskScheduler.Default);
    }
    public void Dispose(){Lock();cancel.Dispose();client.Dispose();}
}
