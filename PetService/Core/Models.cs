namespace PetService.Core;

public sealed class Schedule
{
    public ResponseOptions? Responses {get;set;}
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Time { get; set; } = "";
    public string TimeZone { get; set; } = "";
    public bool Enabled { get; set; }
    public string CreatedAtUtc { get; set; } = "";
}
public sealed class Prompt
{
    public ResponseOptions? Responses {get;set;}
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? ReminderId { get; set; }
    public string? LocalDay { get; set; }
    public string Label { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> Choices { get; set; } = [];
    public bool AllowReply { get; set; } = true;
    public string DueAtUtc { get; set; } = "";
    public string? DisplayedAtUtc { get; set; }
    public string? SnoozedUntilUtc { get; set; }
    public string? ResolvedAtUtc { get; set; }
    public string? Resolution { get; set; }
}
public sealed class Choice
{
    public long? AccessRevision {get;set;}
    public string? ResponseRevision {get;set;}
    public string? OptionId {get;set;}
    public string? SnoozeMode {get;set;}
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OccurrenceId { get; set; } = "";
    public string Action { get; set; } = "";
    public string ClientAtUtc { get; set; } = "";
    public int? Minutes { get; set; }
    public string? Reply { get; set; }
    public string? Request { get; set; }
    public int? OptionIndex { get; set; }
}
public sealed class Observation
{
    [System.Text.Json.Serialization.JsonIgnore][Newtonsoft.Json.JsonIgnore]public string LocalCharacterId {get;set;}="";
    public string TimeZone { get; set; } = "";
    public bool LoggedIn { get; set; }
    public bool Ready { get; set; }
    public bool InDuty { get; set; }
    public bool InCombat { get; set; }
    public bool IsAfk { get; set; }
    public bool GameIdle { get; set; }
    public bool InputGuardActive { get; set; }
    [System.Text.Json.Serialization.JsonIgnore][Newtonsoft.Json.JsonIgnore]public bool Bonded {get;set;}
    [System.Text.Json.Serialization.JsonIgnore][Newtonsoft.Json.JsonIgnore]public bool Sleeping {get;set;}
    [System.Text.Json.Serialization.JsonIgnore][Newtonsoft.Json.JsonIgnore]public bool PromptActive {get;set;}
    public bool LocalRelease { get; set; }
    public string SessionId { get; set; } = "";
    public string LoginObservedAtUtc { get; set; } = "";
    public string LoginTimeSource { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public string HomeWorld { get; set; } = "";
    public string CurrentWorld { get; set; } = "";
    public string DataCenter { get; set; } = "";
    public string Zone { get; set; } = "";
    public uint MapId { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public string Job { get; set; }="";
    public int Level { get; set; }
    public bool Crafting { get; set; }
    public bool Gathering { get; set; }
    public bool Mounted { get; set; }
    public bool Cutscene { get; set; }
    public bool Unconscious { get; set; }
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
    public FeatureState Features {get;set;}=new();
    public List<UploadRejection> RejectedEvents {get;set;}=[];
    public List<UploadRejection> RejectedActivity {get;set;}=[];
    public List<MasterChatMessage> ChatMessages {get;set;}=[];
    public bool LiveReportsRequested { get; set; }
    public string? FreshReportRequestId { get; set; }
    public string ServerTimeUtc { get; set; } = "";
    public string PetId { get; set; } = "";
    public string PetName { get; set; } = "";
    public List<Schedule> Reminders { get; set; } = [];
    public List<Prompt> Occurrences { get; set; } = [];
    public List<string> AcceptedActivityIds { get; set; }=[];
    public DynamicSettings Settings { get; set; }=new();
    public List<string> AcceptedEventIds { get; set; } = [];
}
public sealed class MasterChatMessage
{
    public string Id {get;set;}="";
    public string Text {get;set;}="";
    public string CreatedAtUtc {get;set;}="";
    public string? ExpiresAtUtc {get;set;}
    public string? DisplayedAtUtc {get;set;}
    public string? ResolvedAtUtc {get;set;}
    public string State {get;set;}="queued";
}
public sealed class DeviceEventResult
{
    public List<UploadRejection> RejectedResults {get;set;}=[];
    public List<string> AcceptedResultIds {get;set;}=[];
    public string PetId { get; set; } = "";
    public List<string> AcceptedEventIds { get; set; } = [];
}
