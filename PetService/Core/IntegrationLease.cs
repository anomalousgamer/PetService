namespace PetService.Core;

public sealed class IntegrationLease
{
    public bool Pending { get; set; }
    public string TitleJson { get; set; } = "";
    public string Id { get; set; } = "";
    public string PetId { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? ExpiresAtUtc { get; set; }
    public List<Guid> StatusIds { get; set; } = [];
}

internal static class IntegrationLifetime
{
    internal static List<IntegrationLease> ForCharacter(IEnumerable<IntegrationLease> leases, string characterId)
        => leases.Where(x => x.CharacterId == characterId).ToList();

    internal static bool Expired(IntegrationLease lease, DateTimeOffset now)
        => lease.ExpiresAtUtc is not null && LocalState.Parse(lease.ExpiresAtUtc) <= now;

    internal static bool CanApply(bool sharingAllowed, bool ready, bool localRelease, bool cleanupPending)
        => sharingAllowed && ready && !localRelease && !cleanupPending;
}
