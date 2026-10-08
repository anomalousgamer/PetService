namespace PetService.Core;

internal sealed class AdminDashboard
{
    public MasterIdentity Identity {get;set;}=new();
    public string ServerTimeUtc { get; set; }="";
    public List<AdminProfile> Profiles { get; set; }=[];
    public AdminPet? Selected { get; set; }
    public AdminService Service { get; set; }=new();
}
internal class AdminProfile
{
    public string Id { get; set; }="";
    public string Name { get; set; }="";
    public bool Archived {get;set;}
    public bool Paired { get; set; }
    public string? PairingExpiresAtUtc { get; set; }
    public int PendingCount { get; set; }
    public AdminObservation Status { get; set; }=new();
}
internal sealed class AdminPet : AdminProfile
{
    public FeatureState Features {get;set;}=new();
    public List<MasterChatMessage> ChatMessages {get;set;}=[];
    public TravelStats TravelStats {get;set;}=new();
    public ReportState? Report { get; set; }
    public List<AdminReminder> Reminders { get; set; }=[];
    public List<Prompt> Pending { get; set; }=[];
    public List<ActivityRecord> Timeline {get;set;}=[];
    public List<AttentionItem> Attention {get;set;}=[];
    public DynamicSettings Settings {get;set;}=new();
    public PlaytimeStats Stats {get;set;}=new();
    public List<AdminActivity> Activity { get; set; }=[];
}
internal sealed class AdminReminder
{
    public ResponseOptions? Responses {get;set;}
    public string Id { get; set; }="";
    public string Label { get; set; }="";
    public string Time { get; set; }="";
    public string TimeZone { get; set; }="";
    public bool Enabled { get; set; }
    public string NextDueAtUtc { get; set; }="";
}
internal sealed class AdminObservation
{
    public string State {get;set;}="";
    public string? LastSeenAtUtc { get; set; }
    public string? SnapshotAtUtc { get; set; }
    public Observation? Reported { get; set; }
}
internal sealed class AdminService
{
    public string Version { get; set; }="";
    public bool DatabaseReady { get; set; }
    public bool DiscordReady { get; set; }
}
internal sealed class AdminActivity
{
    public string? OptionLabel {get;set;}
    public string? SnoozeMode {get;set;}
    public string Action { get; set; }="";
    public string Label { get; set; }="";
    public string? Reply { get; set; }
    public int? Minutes { get; set; }
    public string ReceivedAtUtc { get; set; }="";
}
internal sealed record AdminAction
{
    public object? Data {get;init;}
    public string Action { get; init; }="";
    public string RequestId { get; init; }=Guid.NewGuid().ToString();
    public string? Pet { get; init; }
    public DynamicSettings? Settings {get;init;}
    public string? EventId {get;init;}
    public string? Confirmation {get;init;}
    public string? Name { get; init; }
    public string? Label { get; init; }
    public string? Time { get; init; }
    public string? TimeZone { get; init; }
    public string? ReminderId { get; init; }
    public string? PromptId { get; init; }
    public string? Text { get; init; }
    public int? ExpiresMinutes {get;init;}
    public string? MessageId {get;init;}
    public List<string>? Choices { get; init; }
    public bool? AllowReply { get; init; }
}
internal sealed class AdminActionResult
{
    public string Action { get; set; }="";
    public string Pet { get; set; }="";
    public string? Code { get; set; }
    public string? ExpiresAtUtc { get; set; }
    public string? RequestedAtUtc { get; set; }
    public DynamicSettings? Settings {get;set;}
}
internal sealed class SettingsSnapshot
{
    public string Section {get;set;}="controls";
    public MasterIdentity? Identity {get;set;}
    public FeatureState? Controls {get;set;}
    public string Pet {get;set;}="";
    public string ServerTimeUtc {get;set;}="";
    public DynamicSettings Settings {get;set;}=new();
}
internal sealed class ReportState
{
    public string RequestId {get;set;}="";
    public string RequestedAtUtc {get;set;}="";
    public string? CompletedAtUtc {get;set;}
}
internal sealed class LiveReport
{
    public Presence? Presence {get;set;}
    public GameplayLock? Lock {get;set;}
    public List<MasterChatMessage>? ChatMessages {get;set;}
    public string ServerTimeUtc {get;set;}="";
    public AdminObservation Status {get;set;}=new();
    public ReportState? Report {get;set;}
}

internal sealed class HistoryPage {public List<ActivityRecord> Records {get;set;}=[];public string? NextCursor {get;set;}}
internal sealed class TravelStats
{
    public int Today {get;set;}
    public int Week {get;set;}
    public int Month {get;set;}
    public int Total {get;set;}
    public List<TravelGroup> Methods {get;set;}=[];
    public List<TravelGroup> Destinations {get;set;}=[];
    public List<TravelGroup> Characters {get;set;}=[];
}
internal sealed class TravelGroup {public string Label {get;set;}="";public int Trips {get;set;}}
