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
    private bool settingsVisible;
    internal long SettingsReadCount {get;private set;}
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
    internal bool Busy=>dashboardTask is not null || actionTask is not null || historyTask is not null;
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
    internal void Refresh()
    {
        if(Busy || password.Length==0)return;
        dashboardTask=client.Dashboard(password,Selected,cancel.Token);
    }
    internal void Select(string pet)
    {
        if(Busy || !Unlocked || !MasterPortalPolicy.ValidName(pet))return;
        Hide();requestedReportAt=null;History=[];HistoryCursor="";HistoryKind="";Selected=pet;Refresh();
    }
    private static string Fingerprint(AdminAction action)=>JsonSerializer.Serialize(action with{RequestId=""},ServiceClient.Json);
    internal void Action(AdminAction value)
    {
        if(!Unlocked || Busy)return;
        settingsRevision++;
        var copy=value with{Choices=value.Choices?.ToList()};
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
                    result.Settings.NormalizeContacts();pet.Settings=result.Settings;SettingsReadCount++;Error="";
                    clockOffset=(stamp-DateTimeOffset.UtcNow).TotalMilliseconds;
                }
            } catch(Exception e){if(settingsTaskRevision==settingsRevision){Failure(e);nextSettings=Environment.TickCount64+15000;}}
        }
        if(liveTask is {IsCompleted:true}) {
            var task=liveTask;liveTask=null;
            try {
                var result=task.GetAwaiter().GetResult();
                if(liveTaskWatched && leasedPet!=liveTaskPet && password.Length>0)ObserveFailure(client.Live(password,liveTaskPet,viewerId,"stop",CancellationToken.None));
                if(Dashboard?.Selected is { } pet && liveTaskPet==Selected && pet.Name==Selected) {
                    pet.Status=result.Status;pet.Report=result.Report;
                    if(result.ChatMessages is not null)pet.ChatMessages=result.ChatMessages;
                    var profile=Dashboard.Profiles.FirstOrDefault(p=>p.Name==Selected);if(profile is not null)profile.Status=result.Status;
                    if(DateTimeOffset.TryParse(result.ServerTimeUtc,out var stamp))clockOffset=(stamp-DateTimeOffset.UtcNow).TotalMilliseconds;
                    CheckReport();
                }
            } catch(Exception e){Failure(e);nextLive=Environment.TickCount64+15000;}
        }
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
                Notice=activeAction?.Action switch{"sendChat"=>"Master chat message queued.","cancelChat"=>"Chat cancellation saved.","requestReport"=>"Fresh report requested. Waiting for the pet's plugin…","sendMessage"=>"Message queued.","createPet"=>"Pet profile created.","pair"=>"Pairing code generated.","revoke"=>"Device revoked.","addReminder"=>"Daily reminder added.","disableReminder"=>"Reminder disabled.","cancelPrompt"=>"Pending prompt cancelled.",_=>"Saved."};
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
    internal void TickVisible(bool overview,bool chats=false,bool settings=false)
    {
        var live=overview && AutoRefresh;
        if(!live)StopLive();
        if(settings && !settingsVisible)nextSettings=0;
        settingsVisible=settings;
        if(settings && Unlocked && Selected.Length>0 && !Busy && settingsTask is null && Environment.TickCount64>=nextSettings) {
            settingsTaskPet=Selected;settingsTaskRevision=settingsRevision;nextSettings=Environment.TickCount64+5000;
            settingsTask=client.Settings(password,Selected,cancel.Token);
        }
        if(!Unlocked || Selected.Length==0 || liveTask is not null || Busy || (!live && !chats && requestedReportAt is null) || Environment.TickCount64<nextLive)return;
        liveTaskPet=Selected;liveTaskWatched=live;nextLive=Environment.TickCount64+5000;
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
            "CHAT_QUEUE_FULL"=>"This pet already has 100 queued chat messages.","INVALID_CHAT_EXPIRY"=>"Choose 0–10,080 minutes for message expiration.","PET_NOT_PAIRED"=>"Pair this pet before requesting a fresh report.","PET_NAME_EXISTS"=>"A pet with that name already exists.","INVALID_PET_NAME"=>"Use a lowercase name with letters, numbers, hyphens or underscores.",
            "PET_NOT_FOUND"=>"This pet could not be found. Lock and reopen the portal.","PET_TIME_ZONE_UNKNOWN"=>"Connect this pet's plugin once before adding a daily reminder.",
            "INVALID_REPLY_CHOICES"=>"Check the button labels: up to 32 different labels, 80 characters each.","INVALID_REPLY_OPTIONS"=>"Add a reply button or allow a written reply.",
            "REMINDER_LIMIT"=>"This pet already has 32 enabled reminders.","MESSAGE_QUEUE_FULL"=>"This pet already has 50 pending messages.","REQUEST_ID_CONFLICT"=>"The action identifier was already used with different details. Refresh and try again.",
            _=>"Could not complete the request. Refresh or retry; the service may be temporarily unavailable."
        };
        
    }
    internal void Lock()
    {
        Hide();requestedReportAt=null;refreshAfterReport=false;
        cancel.Cancel();cancel.Dispose();cancel=new();
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
