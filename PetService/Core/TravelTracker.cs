namespace PetService.Core;

// Arrival follows an observed loading/zone-initialization signal. Coordinates
// alone never start a journey, including when the destination is the same map.
internal sealed class TravelTracker
{
    private Observation? lastReady,origin;
    private string intent="",method="";
    private long intentUntil,started,settleUntil;
    private bool transitioning,wasLoading;
    private uint aetheryteId;
    private bool casting;
    private long lastArrival=long.MinValue/2;
    private readonly Queue<Dictionary<string,object?>> arrivals=[];
    internal void Intent(string value,long tick,uint destination=0)
    {
        if(transitioning && settleUntil>0 && !wasLoading && lastReady is not null)Complete(lastReady,tick);
        intent=value;intentUntil=tick+20000;if(destination>0 || value=="return")aetheryteId=destination;
    }
    internal void Casting(uint action,long tick)
    {
        if(action is 5 or 6){casting=true;Intent(action==6?"return":"teleport",tick);}
        else if(casting){casting=false;if(!transitioning)intentUntil=tick+1000;}
    }
    internal void Begin(long tick)
    {
        if(transitioning && settleUntil>0 && !wasLoading && lastReady is not null)Complete(lastReady,tick);
        if(transitioning || lastReady is null)return;
        origin=lastReady;started=tick;transitioning=true;settleUntil=0;
        method=tick<=intentUntil?intent:"";
    }
    internal void Initialized(long tick)=>Begin(tick);
    internal void ConfirmTeleport(long tick,uint destination)
    {
        if(!transitioning && tick-lastArrival<2000)return;
        if(transitioning){method="teleport";aetheryteId=destination;}
        else Intent("teleport",tick,destination);
    }
    internal Dictionary<string,object?>? Observe(Observation value,bool loading,long tick)
    {
        if(!value.LoggedIn || (lastReady is not null && lastReady.SessionId!=value.SessionId)){Reset();}
        if(loading && !wasLoading)Begin(tick);
        wasLoading=loading;
        if(transitioning && value.Ready && !loading && origin is not null) {
            if(settleUntil==0)settleUntil=tick+300;
            if(tick>=settleUntil)Complete(value,tick);
        }
        if(value.Ready && !loading)lastReady=value;
        if(transitioning && tick-started>180000)Reset();
        return arrivals.Count>0?arrivals.Dequeue():null;
    }
    private void Complete(Observation value,long tick)
    {
        if(origin is null)return;
        var same=origin.CurrentWorld==value.CurrentWorld && origin.TerritoryId==value.TerritoryId && origin.MapId==value.MapId;
        arrivals.Enqueue(new(){["method"]=method.Length>0?method:same?"area-transfer":"zone-transition",["outcome"]="arrival-observed",
                    ["fromZone"]=origin.Zone,["fromWorld"]=origin.CurrentWorld,["fromTerritoryId"]=origin.TerritoryId,["fromMapId"]=origin.MapId,["fromX"]=origin.X,["fromY"]=origin.Y,
                    ["toZone"]=value.Zone,["toWorld"]=value.CurrentWorld,["toTerritoryId"]=value.TerritoryId,["toMapId"]=value.MapId,["toX"]=value.X,["toY"]=value.Y,
                    ["sameZone"]=same,["travelSeconds"]=Math.Round((tick-started)/1000d,3),["aetheryteId"]=aetheryteId>0?(uint?)aetheryteId:null});
        lastArrival=tick;transitioning=false;origin=null;intent="";method="";intentUntil=0;aetheryteId=0;
    }
    internal void Reset(){arrivals.Clear();lastReady=null;origin=null;intent="";method="";intentUntil=0;started=0;settleUntil=0;transitioning=false;wasLoading=false;aetheryteId=0;casting=false;lastArrival=long.MinValue/2;}
    internal void AetheryteTransfer(long tick){if(transitioning && settleUntil>0 && !wasLoading && lastReady is not null)Complete(lastReady,tick);if(tick>intentUntil)Intent("aethernet",tick);}
}
