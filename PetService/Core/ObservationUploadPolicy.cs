using System.Text.Json;

namespace PetService.Core;

internal sealed class ObservationUploadPolicy
{
    private string? acceptedState;
    internal bool NeedsSnapshot(Observation current,long tick,bool hasActivity,bool hasReplies,bool liveRequested=false,bool freshRequested=false)
        => acceptedState is null || hasActivity || hasReplies || liveRequested || freshRequested || State(current)!=acceptedState;

    internal void Accepted(Observation snapshot,long tick)
    {acceptedState=State(snapshot);}

    internal void Reset()=>acceptedState=null;

    // Walking updates live reports for active viewers, but creates no history.
    // With no viewer, only events, replies and explicit report requests send status.
    private static string State(Observation o)=>JsonSerializer.Serialize(new {
        o.TimeZone,o.LoggedIn,o.Ready,o.SessionId,o.LoginObservedAtUtc,o.LoginTimeSource,
        o.CharacterName,o.HomeWorld,o.CurrentWorld,o.DataCenter,o.Zone,o.TerritoryId,o.MapId,
        o.Job,o.Level,o.InDuty,o.InCombat,o.IsAfk,o.GameIdle,o.Crafting,o.Gathering,
        o.Mounted,o.Cutscene,o.Unconscious,o.InputGuardActive,o.LocalRelease
    });
}
