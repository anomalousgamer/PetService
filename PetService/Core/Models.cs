namespace PetService.Core;

public sealed class Schedule
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Time { get; set; } = "";
    public string TimeZone { get; set; } = "";
    public bool Enabled { get; set; }
    public string CreatedAtUtc { get; set; } = "";
}
public sealed class Prompt
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? ReminderId { get; set; }
    public string? LocalDay { get; set; }
    public string Label { get; set; } = "";
    public string Text { get; set; } = "";
    public string DueAtUtc { get; set; } = "";
    public string? DisplayedAtUtc { get; set; }
    public string? SnoozedUntilUtc { get; set; }
    public string? ResolvedAtUtc { get; set; }
    public string? Resolution { get; set; }
}
public sealed class Choice
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OccurrenceId { get; set; } = "";
    public string Action { get; set; } = "";
    public string ClientAtUtc { get; set; } = "";
    public int? Minutes { get; set; }
    public string? Reply { get; set; }
}
public sealed class Observation
{
    public string TimeZone { get; set; } = "";
    public bool LoggedIn { get; set; }
    public bool Ready { get; set; }
    public bool InDuty { get; set; }
    public bool InCombat { get; set; }
    public bool IsAfk { get; set; }
    public bool GameIdle { get; set; }
    public bool InputGuardActive { get; set; }
    public bool LocalRelease { get; set; }
    public string SessionId { get; set; } = "";
    public string LoginObservedAtUtc { get; set; } = "";
    public string LoginTimeSource { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public string HomeWorld { get; set; } = "";
    public string CurrentWorld { get; set; } = "";
    public string DataCenter { get; set; } = "";
    public string Zone { get; set; } = "";
    public uint TerritoryId { get; set; }
}
public sealed class PairResult
{
    public string PetId { get; set; } = "";
    public string PetName { get; set; } = "";
    public string Token { get; set; } = "";
}
public sealed class SyncResult
{
    public string ServerTimeUtc { get; set; } = "";
    public string PetId { get; set; } = "";
    public string PetName { get; set; } = "";
    public List<Schedule> Reminders { get; set; } = [];
    public List<Prompt> Occurrences { get; set; } = [];
    public List<string> AcceptedEventIds { get; set; } = [];
}
public sealed class DeviceEventResult
{
    public string PetId { get; set; } = "";
    public List<string> AcceptedEventIds { get; set; } = [];
}
