using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using PetService.Core;

namespace PetService;

internal sealed unsafe class TradeRecorder : IDisposable
{
    private readonly Plugin plugin;
    private readonly Action<TradeResult> record;
    private readonly TradeTracker tracker=new();
    private readonly ConcurrentQueue<Signal> signals=[];
    private readonly HashSet<(nint,uint)> seen=[];
    private Hook<RaptureLogModule.Delegates.Update>? logUpdate;
    private Hook<RaptureLogModule.Delegates.ShowLogMessage>? simpleMessage;
    private sealed record Signal(uint Id,TradeSnapshot Snapshot,string Session,int Revision,long Tick,DateTimeOffset At);
    private int revision;
    private long nextPoll;

    internal TradeRecorder(Plugin plugin,Action<TradeResult> record)
    {
        this.plugin=plugin;this.record=record;
        try {
            logUpdate=Plugin.Interop.HookFromAddress<RaptureLogModule.Delegates.Update>((nint)RaptureLogModule.MemberFunctionPointers.Update,LogUpdate);
            logUpdate.Enable();
        } catch(Exception e) {
            logUpdate?.Dispose();logUpdate=null;Plugin.Log.Warning(e,"Trade result queue observation is unavailable.");
            try {
                simpleMessage=Plugin.Interop.HookFromAddress<RaptureLogModule.Delegates.ShowLogMessage>((nint)RaptureLogModule.MemberFunctionPointers.ShowLogMessage,SimpleMessage);
                simpleMessage.Enable();
            } catch(Exception fallback) {simpleMessage?.Dispose();simpleMessage=null;Plugin.Log.Warning(fallback,"Trade outcomes will remain unconfirmed when no result event is observed.");}
        }
    }
    private bool Enabled=>plugin.IsPaired && !plugin.KillSwitchOn && plugin.Configuration.ActivityOutbox.Count<50000;
    private void Enqueue(uint id)
    {
        if(!Enabled || !TradeTracker.IsSignal(id) || signals.Count>=32)return;
        var generation=Volatile.Read(ref revision);
        signals.Enqueue(new(id,Capture(),plugin.ObservedSessionId,generation,Environment.TickCount64,DateTimeOffset.UtcNow));
    }
    private void SimpleMessage(RaptureLogModule* self,uint id)
    {try{Enqueue(id);}catch{}simpleMessage!.Original(self,id);}
    private void LogUpdate(RaptureLogModule* self)
    {
        try {
            if(Enabled && self->LogMessageQueue.LongCount<=512) {
                for(var i=0L;i<self->LogMessageQueue.LongCount;i++) {
                    ref var entry=ref self->LogMessageQueue[i];
                    // Read only the fixed event ID. No text parameters, sender,
                    // target, chat entries, or partner identity are inspected.
                    var id=entry.LogMessageId;
                    if(TradeTracker.IsSignal(id) && seen.Add(((nint)Unsafe.AsPointer(ref entry),id)))Enqueue(id);
                }
            }
        } catch{}
        logUpdate!.Original(self);
        if(seen.Count==0)return;
        try {
            var remaining=new HashSet<(nint,uint)>();
            if(Enabled && self->LogMessageQueue.LongCount<=512)for(var i=0L;i<self->LogMessageQueue.LongCount;i++) {
                ref var entry=ref self->LogMessageQueue[i];var id=entry.LogMessageId;
                if(TradeTracker.IsSignal(id))remaining.Add(((nint)Unsafe.AsPointer(ref entry),id));
            }
            seen.IntersectWith(remaining);
        } catch{seen.Clear();}
    }
    private static TradeSide Side(Span<InventoryItem> slots)=>TradeOfferReader.Read(slots,
        id=>Plugin.Data.GetExcelSheet<Item>()?.GetRowOrDefault(id)?.Name.ToString()??"");
    private static TradeSnapshot Capture()
    {
        var agent=AgentTrade.Instance();
        if(agent==null || !agent->IsAgentActive())return TradeSnapshot.Closed;
        var manager=InventoryManager.Instance();
        if(manager==null)return new(true,false,new([],null),new([],null));
        // These arrays are also reused by unrelated hand-in/mail screens.
        // Require the Trade agent and mapped trading state before reading.
        var valid=manager->TradeLocalState is >=TradeState.SelectingTradeGoods and <=TradeState.Confirmed
            || manager->TradeRemoteState is >=TradeState.SelectingTradeGoods and <=TradeState.Confirmed;
        return valid?new(true,true,Side(manager->TradeItemsLocal),Side(manager->TradeItemsRemote)):
            new(true,false,new([],null),new([],null));
    }
    internal void Update(Func<Observation> observe)
    {
        if(!Enabled){Reset();return;}
        var tick=Environment.TickCount64;if(tick<nextPoll && signals.IsEmpty)return;nextPoll=tick+100;
        TradeSnapshot snapshot;
        try{snapshot=Capture();}catch{return;}
        if(!snapshot.WindowOpen && !tracker.NeedsObservation && signals.IsEmpty)return;
        var context=observe();var at=DateTimeOffset.UtcNow;
        if(!context.LoggedIn || context.SessionId.Length==0){Stop("Character logged out");return;}
        while(signals.TryDequeue(out var signal))if(signal.Revision==Volatile.Read(ref revision) && signal.Session==context.SessionId)
            tracker.Signal(signal.Id,signal.Snapshot,context,signal.Tick,signal.At);
        tracker.Observe(snapshot,context,tick,at);
        Deliver();
    }
    private void Deliver()
    {
        foreach(var result in tracker.Drain())record(result);
    }
    internal void Stop(string reason){tracker.Interrupt(DateTimeOffset.UtcNow,reason);Deliver();Reset();}
    internal void Reset(){Interlocked.Increment(ref revision);tracker.Reset();while(signals.TryDequeue(out _)){}nextPoll=0;}
    public void Dispose(){logUpdate?.Dispose();simpleMessage?.Dispose();Reset();}
}
