namespace PetService.Core;

internal sealed record TradeItem(uint ItemId,string Name,uint? Quantity,bool Hq,int Slot);
internal sealed record TradeSide(List<TradeItem> Items,uint? Gil);
internal sealed record TradeSnapshot(bool WindowOpen,bool OffersAvailable,TradeSide Give,TradeSide Receive)
{
    internal static TradeSnapshot Closed=>new(false,false,new([],null),new([],null));
}
internal sealed record TradeResult(Dictionary<string,object?> Data,Observation Context,DateTimeOffset At);

// One terminal record per observed trade. A confirmation click or a closed
// window alone is never promoted to a completed transaction.
internal sealed class TradeTracker
{
    private sealed class Entry(Observation context,DateTimeOffset at)
    {
        internal readonly string Id=Guid.NewGuid().ToString();
        internal readonly Observation Context=context;
        internal readonly DateTimeOffset Started=at;
        internal TradeSnapshot? Offer;
        internal long? ClosedAt;
        internal DateTimeOffset? ClosedStamp;
    }
    private Entry? current;
    private bool terminalWhileOpen;
    private readonly Queue<TradeResult> results=[];
    internal bool HasTrade=>current is not null;
    internal bool NeedsObservation=>HasTrade||terminalWhileOpen;
    internal static bool IsSignal(uint id)=>id is 35 or 36 or 38 or 39 or 40 or 42 or 43 or 46 or 47 or 54;
    internal void Observe(TradeSnapshot snapshot,Observation context,long tick,DateTimeOffset at)
    {
        if(current is not null && current.Context.SessionId!=context.SessionId)Interrupt(at,"Character session changed");
        if(!snapshot.WindowOpen)terminalWhileOpen=false;
        if(terminalWhileOpen)return;
        if(snapshot.WindowOpen) {
            if(current?.ClosedAt is not null)Finish("unconfirmed","window-closed","No result event was observed",current.ClosedStamp??at);
            current??=new(context,at);
            if(snapshot.OffersAvailable)current.Offer=snapshot;
        } else if(current is not null) {
            current.ClosedAt??=tick;current.ClosedStamp??=at;
            // Allow a terminal system event to arrive after the window closes.
            if(tick-current.ClosedAt.Value>=3000)Finish("unconfirmed","window-closed","No result event was observed",current.ClosedStamp.Value);
        }
    }
    internal void Signal(uint id,TradeSnapshot snapshot,Observation context,long tick,DateTimeOffset at)
    {
        if(!IsSignal(id))return;
        if(current is not null && current.Context.SessionId!=context.SessionId)Interrupt(at,"Character session changed");
        if(id==35){terminalWhileOpen=false;if(!HasTrade && snapshot.WindowOpen)Observe(snapshot,context,tick,at);return;}
        if(terminalWhileOpen)return;
        if(current is null) {
            if(!snapshot.WindowOpen)return; // No fabricated trade from a stray result.
            current=new(context,at);
        }
        // Native offers may already have been cleared before the result is
        // queued. Preserve the most recent observed contents in that case.
        if(snapshot.OffersAvailable && (snapshot.Give.Items.Count+snapshot.Receive.Items.Count>0
            || snapshot.Give.Gil>0 || snapshot.Receive.Gil>0 || current.Offer is null))current.Offer=snapshot;
        Finish(id==38?"completed":id==36?"cancelled":"failed","system-event",id switch {
            36=>"Trade canceled",39 or 40=>"Insufficient inventory space",42 or 43=>"Unique-item restriction",
            46 or 47=>"Gil limit reached",54=>"Trade could not be completed",_=>""
        },at);
    }
    internal void Interrupt(DateTimeOffset at,string reason)
    {if(current is not null)Finish("interrupted","observation-ended",reason,at);}
    private void Finish(string outcome,string evidence,string reason,DateTimeOffset at)
    {
        if(current is not { } entry)return;
        var offer=entry.Offer;
        // A successful player trade cannot have an empty offer on both sides.
        // If sampling saw only cleared/empty fields, amounts remain unknown.
        if(outcome=="completed" && offer is not null && offer.Give.Items.Count+offer.Receive.Items.Count==0
            && !(offer.Give.Gil>0) && !(offer.Receive.Gil>0))offer=null;
        results.Enqueue(new(new(){["tradeId"]=entry.Id,["phase"]="finished",["outcome"]=outcome,
            ["startedAtUtc"]=LocalState.Stamp(entry.Started),["endedAtUtc"]=LocalState.Stamp(at),
            ["giveItems"]=offer?.Give.Items??[],["receiveItems"]=offer?.Receive.Items??[],
            ["giveGil"]=offer?.Give.Gil,["receiveGil"]=offer?.Receive.Gil,
            ["offersKnown"]=offer is not null,["evidence"]=evidence,["reason"]=reason},entry.Context,at));
        current=null;
        if(evidence=="system-event")terminalWhileOpen=true;
    }
    internal IEnumerable<TradeResult> Drain(){while(results.TryDequeue(out var result))yield return result;}
    internal void Reset(){current=null;results.Clear();terminalWhileOpen=false;}
}
