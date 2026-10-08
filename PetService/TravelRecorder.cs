using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using PetService.Core;

namespace PetService;

internal sealed unsafe class TravelRecorder : IDisposable
{
    private readonly Plugin plugin;
    private readonly TravelTracker tracker=new();
    private Hook<Telepo.Delegates.Teleport>? teleport;
    private Hook<ActionManager.Delegates.UseAction>? action;
    private Hook<TeleportHistoryModule.Delegates.AddToHistory>? history;
    private long nextSample;
    internal TravelRecorder(Plugin plugin)
    {
        this.plugin=plugin;
        Plugin.Condition.ConditionChange+=ConditionChanged;
        Plugin.ClientState.ZoneInit+=ZoneInitialized;
        try {teleport=Plugin.Interop.HookFromAddress<Telepo.Delegates.Teleport>((nint)Telepo.MemberFunctionPointers.Teleport,Teleport);teleport.Enable();}
        catch(Exception e){teleport?.Dispose();teleport=null;Plugin.Log.Warning(e,"Teleport intent observation is unavailable.");}
        try {action=Plugin.Interop.HookFromAddress<ActionManager.Delegates.UseAction>((nint)ActionManager.MemberFunctionPointers.UseAction,UseAction);action.Enable();}
        catch(Exception e){action?.Dispose();action=null;Plugin.Log.Warning(e,"Return intent observation is unavailable.");}
        try {history=Plugin.Interop.HookFromAddress<TeleportHistoryModule.Delegates.AddToHistory>((nint)TeleportHistoryModule.MemberFunctionPointers.AddToHistory,AddedToHistory);history.Enable();}
        catch(Exception e){history?.Dispose();history=null;Plugin.Log.Warning(e,"Teleport completion observation is unavailable; loading/zone arrivals remain observed.");}
    }
    private bool Active=>plugin.SharingAllowed && Plugin.ClientState.IsLoggedIn;
    private bool Teleport(Telepo* self,uint id,byte index)
    {
        var result=teleport!.Original(self,id,index);
        try {if(result && Active)tracker.Intent("teleport",Environment.TickCount64,id);}catch{}
        return result;
    }
    private bool UseAction(ActionManager* self,ActionType type,uint id,ulong target,uint param,ActionManager.UseActionMode mode,uint route,bool* started)
    {
        var result=action!.Original(self,type,id,target,param,mode,route,started);
        try {if(result && Active && type==ActionType.Action && id is 5 or 6)tracker.Intent(id==6?"return":"teleport",Environment.TickCount64);}catch{}
        return result;
    }
    private uint AddedToHistory(TeleportHistoryModule* self,ushort id,bool unused,byte ward,byte plot,byte index)
    {
        var result=history!.Original(self,id,unused,ward,plot,index);
        try {if(Active)tracker.ConfirmTeleport(Environment.TickCount64,id);}catch{}
        return result;
    }
    private void ConditionChanged(ConditionFlag flag,bool value)
    {
        if(!Active || !value || flag is not (ConditionFlag.BetweenAreas or ConditionFlag.BetweenAreas51))return;
        try {
            var events=EventFramework.Instance();
            if(events!=null && (events->EventState1.EventId.ContentId==EventHandlerContent.Aetheryte || events->EventState2.EventId.ContentId==EventHandlerContent.Aetheryte))tracker.AetheryteTransfer(Environment.TickCount64);
        } catch{}
        tracker.Begin(Environment.TickCount64);
    }
    private void ZoneInitialized(ZoneInitEventArgs args){if(Active)tracker.Initialized(Environment.TickCount64);}
    internal void Update(Func<Observation> observe)
    {
        if(!Active){tracker.Reset();return;}
        var tick=Environment.TickCount64;if(tick<nextSample)return;nextSample=tick+100;
        var o=observe();
        tracker.Casting(Plugin.Objects.LocalPlayer is {IsCasting:true} player?player.CastActionId:0,tick);
        var loading=Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51];
        if(tracker.Observe(o,loading,tick) is { } data)plugin.RecordTravel(data,o);
    }
    internal void Reset()=>tracker.Reset();
    public void Dispose(){Plugin.Condition.ConditionChange-=ConditionChanged;Plugin.ClientState.ZoneInit-=ZoneInitialized;history?.Dispose();action?.Dispose();teleport?.Dispose();}
}
