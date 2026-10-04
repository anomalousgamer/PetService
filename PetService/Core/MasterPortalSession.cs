using System.Text.Json;

namespace PetService.Core;

internal sealed class MasterPortalSession(MasterPortalClient client) : IDisposable
{
    private string password="";
    private CancellationTokenSource cancel=new();
    private Task<AdminDashboard>? dashboardTask;
    private Task<AdminActionResult>? actionTask;
    private AdminAction? activeAction,retryAction;
    private long nextRefresh;
    private double clockOffset;
    internal AdminDashboard? Dashboard { get; private set; }
    internal string Selected { get; private set; }="";
    internal string Error { get; private set; }="";
    internal string Notice { get; private set; }="";
    internal bool Unlocked=>password.Length>0 && Dashboard is not null;
    internal bool Busy=>dashboardTask is not null || actionTask is not null;
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
        dashboardTask=client.Dashboard(password,Selected,cancel.Token);nextRefresh=Environment.TickCount64+15000;
    }
    internal void Select(string pet)
    {
        if(Busy || !Unlocked || !MasterPortalPolicy.ValidName(pet))return;
        Selected=pet;Refresh();
    }
    private static string Fingerprint(AdminAction action)=>JsonSerializer.Serialize(action with{RequestId=""},ServiceClient.Json);
    internal void Action(AdminAction value)
    {
        if(!Unlocked || Busy)return;
        var copy=value with{Choices=value.Choices?.ToList()};
        activeAction=retryAction is not null && Fingerprint(retryAction)==Fingerprint(copy) ? retryAction : copy;
        retryAction=activeAction;Error="";Notice="";
        actionTask=client.Action(password,activeAction,cancel.Token);
    }
    internal void Retry(){if(CanRetry)Action(retryAction!);}
    internal void Update()
    {
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
                Notice=activeAction?.Action switch{"sendMessage"=>"Message queued.","createPet"=>"Pet profile created.","pair"=>"Pairing code generated.","revoke"=>"Device revoked.","addReminder"=>"Daily reminder added.","disableReminder"=>"Reminder disabled.","cancelPrompt"=>"Pending prompt cancelled.",_=>"Saved."};
                if(activeAction?.Action=="createPet")Selected=CompletedResult.Pet;
                Refresh();
            } catch(Exception exception){Failure(exception);}
            activeAction=null;
        }
        if(Unlocked && AutoRefresh && !Busy && Environment.TickCount64>=nextRefresh)Refresh();
    }
    internal void ClearCompletion(){CompletedAction=null;CompletedResult=null;}
    private void Failure(Exception exception)
    {
        if(exception is AdminFailure{Unauthorized:true}){Lock();Error="Administrator password was not accepted. Unlock again.";return;}
        Error=exception.Message switch {
            "PET_NAME_EXISTS"=>"A pet with that name already exists.","INVALID_PET_NAME"=>"Use a lowercase name with letters, numbers, hyphens or underscores.",
            "PET_NOT_FOUND"=>"This pet could not be found. Lock and reopen the portal.","PET_TIME_ZONE_UNKNOWN"=>"Connect this pet's plugin once before adding a daily reminder.",
            "INVALID_REPLY_CHOICES"=>"Check the button labels: up to 32 different labels, 80 characters each.","INVALID_REPLY_OPTIONS"=>"Add a reply button or allow a written reply.",
            "REMINDER_LIMIT"=>"This pet already has 32 enabled reminders.","MESSAGE_QUEUE_FULL"=>"This pet already has 50 pending messages.","REQUEST_ID_CONFLICT"=>"The action identifier was already used with different details. Refresh and try again.",
            _=>"Could not complete the request. Refresh or retry; the service may be temporarily unavailable."
        };
        nextRefresh=Environment.TickCount64+15000;
    }
    internal void Lock()
    {
        cancel.Cancel();cancel.Dispose();cancel=new();
        ObserveFailure(dashboardTask);ObserveFailure(actionTask);dashboardTask=null;actionTask=null;
        password="";Dashboard=null;Selected="";activeAction=null;retryAction=null;CompletedAction=null;CompletedResult=null;
        Error="";Notice="";clockOffset=0;nextRefresh=0;
    }
    private static void ObserveFailure(Task? task)
    {
        if(task is not null)_=task.ContinueWith(t=>{_=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted,TaskScheduler.Default);
    }
    public void Dispose(){Lock();cancel.Dispose();client.Dispose();}
}
