using System.Text.Json;
using Dalamud.Game.DutyState;
using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using PetService.Core;

namespace PetService;

internal sealed unsafe class ActivityRecorder : IDisposable
{
    private readonly Plugin plugin;
    private readonly List<ActivityRecord> pending=[];
    private readonly HashSet<uint> baselined=[];
    private Observation? previous;
    private DateTimeOffset? intervalStart;
    private long nextPoll,nextPosition,nextSave,baselineAfter;
    private string runId="",dutyOutcome="unconfirmed",tradeState="",retainerState="";
    private uint dutyId;
    internal string Error {get;private set;}="";
    internal ActivityRecorder(Plugin plugin)
    {
        this.plugin=plugin;
        Plugin.Duty.DutyStarted+=Started;Plugin.Duty.DutyCompleted+=Completed;
        Plugin.Duty.DutyWiped+=Wiped;Plugin.Duty.DutyRecommenced+=Recommenced;
        Plugin.Inventory.InventoryChanged+=InventoryChanged;
    }
    private bool Active=>plugin.IsPaired && !plugin.KillSwitchOn && plugin.Configuration.ActivityOutbox.Count+pending.Count<50000;
    private void Add(string kind,Dictionary<string,object?> data,Observation? context=null,DateTimeOffset? time=null)
    {
        var o=context??previous;if(!Active || o is null || o.SessionId.Length==0)return;
        pending.Add(new(){Kind=kind,AtUtc=LocalState.Stamp(time??DateTimeOffset.UtcNow),SessionId=o.SessionId,CharacterName=o.CharacterName,HomeWorld=o.HomeWorld,
            Data=data.Where(p=>p.Value is not null).ToDictionary(p=>p.Key,p=>p.Value)});
    }
    private static Dictionary<string,object?> Snapshot(Observation o)=>new(){["zone"]=o.Zone,["world"]=o.CurrentWorld,["dataCenter"]=o.DataCenter,["territoryId"]=o.TerritoryId,["mapId"]=o.MapId,["x"]=o.X,["y"]=o.Y,["job"]=o.Job,["level"]=o.Level,["ready"]=o.Ready,["inDuty"]=o.InDuty,["inCombat"]=o.InCombat,["isAfk"]=o.IsAfk,["gameIdle"]=o.GameIdle,["crafting"]=o.Crafting,["gathering"]=o.Gathering,["mounted"]=o.Mounted,["cutscene"]=o.Cutscene,["unconscious"]=o.Unconscious};
    private void FlushInterval(DateTimeOffset stamp)
    {
        if(intervalStart is { } start && previous is {LoggedIn:true}) {
            var seconds=(stamp-start).TotalSeconds;
            if(seconds>0 && seconds<=30){var data=Snapshot(previous);data["fromUtc"]=LocalState.Stamp(start);data["seconds"]=Math.Round(seconds,3);Add("interval",data,previous,stamp);}
            else if(seconds>30 || seconds<0)Add("gap",new(){["reason"]="Framework or clock interruption; duration unknown"},previous,stamp);
        }
        intervalStart=stamp;
    }
    internal void Update(Func<Observation> observe)
    {
        if(!Active){if(plugin.Configuration.ActivityOutbox.Count+pending.Count>=50000)Error="Local activity queue is full. Recording pauses until saved observations are accepted.";previous=null;intervalStart=null;baselined.Clear();runId="";dutyId=0;tradeState="";retainerState="";return;}
        var tick=Environment.TickCount64;if(tick<nextPoll)return;nextPoll=tick+1000;
        var o=observe();var stamp=DateTimeOffset.UtcNow;
        if(previous is null || previous.SessionId!=o.SessionId) {
            FlushInterval(stamp);
            if(previous is {LoggedIn:true}) {
                if(runId.Length>0)DutyRecord("interrupted","unknown");
                Add("session",new(){["reason"]=o.LoggedIn?"character changed":"logout observed",["source"]="end"},previous,stamp);
            }
            runId="";dutyId=0;baselined.Clear();baselineAfter=tick+10000;
            if(o.LoggedIn){Add("session",new(){["source"]=o.LoginTimeSource,["reason"]="recording begins"},o,stamp);intervalStart=stamp;}
            else intervalStart=null;
        }
        if(previous?.LoggedIn==true && (intervalStart is null || (stamp-intervalStart.Value).TotalSeconds>=15
            || previous.TerritoryId!=o.TerritoryId || previous.CurrentWorld!=o.CurrentWorld || previous.Job!=o.Job || previous.Ready!=o.Ready))FlushInterval(stamp);
        if(o.Ready) {
            if(previous?.Ready!=true || previous.Zone!=o.Zone || previous.CurrentWorld!=o.CurrentWorld || previous.MapId!=o.MapId){Add("zone",Snapshot(o),o,stamp);nextPosition=0;}
            if(previous?.Job!=o.Job || previous.Level!=o.Level)Add("job",Snapshot(o),o,stamp);
            if(previous is null || StateKey(previous)!=StateKey(o))Add("state",Snapshot(o),o,stamp);
            if(tick>=nextPosition){nextPosition=tick+30000;if(o.X is not null && o.Y is not null)Add("position",Snapshot(o),o,stamp);}
            previous=o;
            var currentDuty=o.InDuty?Plugin.Duty.ContentFinderCondition.RowId:0;
            if(currentDuty!=dutyId) {
                if(runId.Length>0)DutyRecord("departed",dutyOutcome);
                dutyId=currentDuty;runId="";
                if(currentDuty>0){runId=Guid.NewGuid().ToString();dutyOutcome="unconfirmed";DutyRecord(Plugin.Duty.IsDutyStarted?"joined-in-progress":"entered","unconfirmed");}
            }
            if(tick>=baselineAfter)BaselineContainers();
            ObserveNative();
        }
        previous=o;
        if(tick>=nextSave){nextSave=tick+15000;Save();}
        if(plugin.Configuration.ActivityOutbox.Count+pending.Count>=50000)Error="Local activity queue is full. Recording pauses until the service accepts saved observations.";
    }
    private static string StateKey(Observation o)=>$"{o.InDuty}:{o.InCombat}:{o.IsAfk}:{o.GameIdle}:{o.Crafting}:{o.Gathering}:{o.Mounted}:{o.Cutscene}:{o.Unconscious}";
    private void DutyRecord(string phase,string outcome)
    {
        if(runId.Length==0 || dutyId==0)return;
        var name=Plugin.Data.GetExcelSheet<ContentFinderCondition>()?.GetRowOrDefault(dutyId)?.Name.ToString()??"Unknown duty";
        Add("duty",new(){["runId"]=runId,["dutyId"]=dutyId,["name"]=name,["phase"]=phase,["outcome"]=outcome});
    }
    private void EnsureDuty(IDutyStateEventArgs args)
    {
        if(!Active || !plugin.Ready)return;
        if(dutyId!=args.ContentFinderCondition.RowId || runId.Length==0){dutyId=args.ContentFinderCondition.RowId;runId=Guid.NewGuid().ToString();dutyOutcome="unconfirmed";}
    }
    private void Started(IDutyStateEventArgs args){EnsureDuty(args);DutyRecord("started","unconfirmed");}
    private void Completed(IDutyStateEventArgs args){EnsureDuty(args);dutyOutcome="completed";DutyRecord("completed",dutyOutcome);}
    private void Wiped(IDutyStateEventArgs args){EnsureDuty(args);DutyRecord("wipe",dutyOutcome);}
    private void Recommenced(IDutyStateEventArgs args){EnsureDuty(args);DutyRecord("recommenced",dutyOutcome);}
    private static bool OwnContainer(uint type)=>type<=3 || type==1000 || type==2000 || type==2001 || (type>=3200 && type<=3500) || (type>=4000 && type<=4101) || (type>=10000 && type<=12002);
    private void BaselineContainers()
    {
        var manager=InventoryManager.Instance();if(manager==null)return;
        foreach(InventoryType type in Enum.GetValues<InventoryType>()) {
            if(!OwnContainer((uint)type))continue;
            var c=manager->GetInventoryContainer(type);
            if(c!=null && c->IsLoaded)baselined.Add((uint)type);else baselined.Remove((uint)type);
        }
    }
    private void InventoryChanged(IReadOnlyCollection<InventoryEventArgs> events)
    {foreach(var e in events)InventoryItem(e.Type,e);}
    private void InventoryItem(GameInventoryEvent type,InventoryEventArgs args)
    {
        if(!Active || !plugin.Ready || previous is null || Environment.TickCount64<baselineAfter)return;
        var item=args.Item;if(!OwnContainer((uint)item.ContainerType) || !baselined.Contains((uint)item.ContainerType))return;
        var manager=InventoryManager.Instance();var container=manager==null?null:manager->GetInventoryContainer((InventoryType)item.ContainerType);
        if(container==null || !container->IsLoaded)return;
        var data=new Dictionary<string,object?>{["operation"]=type.ToString(),["itemId"]=item.BaseItemId,["name"]=ItemName(item.BaseItemId),["quantity"]=item.Quantity,["hq"]=item.IsHq,["container"]=item.ContainerType.ToString(),["slot"]=item.InventorySlot,["source"]="unconfirmed"};
        if(args is InventoryItemChangedArgs changed)data["oldQuantity"]=changed.OldItemState.Quantity;
        if(args is InventoryComplexEventArgs complex){data["targetContainer"]=complex.TargetEvent.Item.ContainerType.ToString();data["targetSlot"]=complex.TargetEvent.Item.InventorySlot;}
        Add("inventory",data);
    }
    private static string ItemName(uint id)=>Plugin.Data.GetExcelSheet<Item>()?.GetRowOrDefault(id)?.Name.ToString()??$"Item {id}";
    private void ObserveNative()
    {
        // Read only mapped agent fields. Do not read player-name/roster/chat fields.
        try {
            var trade=AgentTrade.Instance();var active=trade!=null && trade->IsAgentActive();
            if(active) {
                var give=trade->ItemsGive.ToArray().Where(i=>i.Id>0).Select(i=>i.Id).ToArray();
                var receive=trade->ItemsReviece.ToArray().Where(i=>i.Id>0).Select(i=>i.Id).ToArray();
                var key=string.Join(',',give)+"|"+string.Join(',',receive);
                if(key!=tradeState) {
                    tradeState=key;
                    Add("trade",new(){["phase"]="give-offer-observed",["outcome"]="quantity/gil/completion unconfirmed",["items"]=give.Select(id=>new {itemId=id%1000000,quantity=(uint?)null,hq=id>=1000000}).ToArray()});
                    Add("trade",new(){["phase"]="receive-offer-observed",["outcome"]="quantity/gil/completion unconfirmed",["items"]=receive.Select(id=>new {itemId=id%1000000,quantity=(uint?)null,hq=id>=1000000}).ToArray()});
                }
            } else if(tradeState.Length>0){Add("trade",new(){["phase"]="window-closed",["outcome"]="unconfirmed"});tradeState="";}
            var retainer=AgentRetainerTask.Instance();
            if(retainer!=null && retainer->IsAgentActive() && retainer->DisplayType==3 && !retainer->IsLoading) {
                var result=retainer->RetainerData;
                var key=$"{result.RewardRetainerTaskId}:{result.RewardXP}:{string.Join(',',result.RewardItemIds.ToArray())}:{string.Join(',',result.RewardItemCount.ToArray())}";
                if(key!=retainerState) {
                    retainerState=key;
                    var items=new List<object>();for(var i=0;i<2;i++)if(result.RewardItemIds[i]>0)items.Add(new {itemId=result.RewardItemIds[i]%1000000,quantity=result.RewardItemCount[i],hq=result.RewardItemIds[i]>=1000000});
                    var manager=RetainerManager.Instance();var activeRetainer=manager==null?null:manager->GetActiveRetainer();
                    var retainerName=activeRetainer==null?"":activeRetainer->NameString;
                    Add("retainer",new(){["name"]=retainerName,["phase"]="result-presented; collection unconfirmed",["taskId"]=result.RewardRetainerTaskId,["xp"]=result.RewardXP,["items"]=items});
                }
            } else retainerState="";
        } catch {Error="Some native activity observations are unavailable on this game build.";}
    }
    internal void Stop(string reason)
    {
        if(previous?.LoggedIn==true && Active){FlushInterval(DateTimeOffset.UtcNow);if(runId.Length>0)DutyRecord("interrupted","unknown");Add("session",new(){["source"]="end",["reason"]=reason});}
        Save();previous=null;intervalStart=null;baselined.Clear();runId="";dutyId=0;
    }
    internal void ClearPairingState()
    {
        pending.Clear();previous=null;intervalStart=null;baselined.Clear();runId="";dutyId=0;
        tradeState="";retainerState="";Error="";nextPoll=0;nextPosition=0;
    }
    private void Save()
    {
        if(pending.Count==0)return;
        if(plugin.Mutate(c=>c.ActivityOutbox.AddRange(pending))){pending.Clear();Error="";}
        else Error="Activity observations could not be saved. Check local disk space.";
    }
    public void Dispose()
    {
        Stop("plugin unloaded");Plugin.Duty.DutyStarted-=Started;Plugin.Duty.DutyCompleted-=Completed;
        Plugin.Duty.DutyWiped-=Wiped;Plugin.Duty.DutyRecommenced-=Recommenced;Plugin.Inventory.InventoryChanged-=InventoryChanged;
    }
}
