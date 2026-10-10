using PetService.Core;

namespace PetService;

internal sealed class SleepController(Plugin plugin,Func<string?>? applyEmote=null)
{
    private string activeId="";
    private long started;
    private bool applied;
    internal bool Active=>activeId.Length>0;
    private void Report(string id,string state,string detail)
    {
        var result=new FeatureResult{Id=id,State=state,Detail=detail,AtUtc=LocalState.Stamp(plugin.Now)};
        if(!plugin.Mutate(c=>{if(!c.FeatureResults.Any(x=>x.Id==id&&x.State==state))c.FeatureResults.Add(result);})){if(!plugin.Configuration.FeatureResults.Any(x=>x.Id==id&&x.State==state))plugin.Configuration.FeatureResults.Add(result);}
        plugin.RequestFeatureSync();
    }
    internal void End(string reason,string state="released")
    {
        if(activeId.Length==0)return;
        var id=activeId;activeId="";
        // End locally before attempting persistence or any network operation.
        plugin.ReleaseInputImmediately();
        if(!plugin.Mutate(c=>SleepPolicy.Remember(c.EndedSleepIds,id)))SleepPolicy.Remember(plugin.Configuration.EndedSleepIds,id);
        Report(id,state,reason);
    }
    internal void Update(bool inputReady)
    {
        var command=plugin.Configuration.Features.Sleep;
        var requested=plugin.SharingAllowed&&plugin.Ready&&!plugin.LocalRelease&&SleepPolicy.Requested(command,plugin.Configuration.EndedSleepIds,plugin.CurrentCharacterId);
        if(Active&&(!requested||command!.Id!=activeId))End("Sleep ended by Master or character/access change.");
        if(command is {Enabled:false,Operation:"wake"} && plugin.SharingAllowed && !plugin.Configuration.EndedSleepIds.Contains(command.Id)){
            if(plugin.Mutate(c=>SleepPolicy.Remember(c.EndedSleepIds,command.Id)))Report(command.Id,"released","Wake applied by Master.");
        }
        if(Active){
            if(!inputReady || Environment.TickCount64-started>2000&&!plugin.InputGuardCapturing){End("Sleep input capture is unavailable; local restrictions ended.","failed");return;}
            if(!applied&&plugin.InputGuardCapturing){applied=true;Report(activeId,"applied","Sleep applied; gameplay and outgoing chat are blocked.");}
            return;
        }
        if(!requested)return;
        var id=command!.Id;
        string? failure=null;
        try {
            if(!inputReady)failure="Required gameplay or chat input guard is unavailable.";
            else failure=(applyEmote??SleepEmote.TryApply)();
        } catch {failure="Play Dead could not be applied on this game build.";}
        if(failure is not null){if(!plugin.Mutate(c=>SleepPolicy.Remember(c.EndedSleepIds,id)))SleepPolicy.Remember(plugin.Configuration.EndedSleepIds,id);Report(id,"failed",failure);return;}
        activeId=id;
        started=Environment.TickCount64;applied=false;
        try{MasterChatDelivery.Print(plugin.Configuration,"Sleep");}catch{Plugin.Log.Warning("The local Sleep announcement could not be shown.");}
    }
}
