namespace PetService.Core;

internal sealed class AdminDashboard
{
    public string ServerTimeUtc { get; set; }="";
    public List<AdminProfile> Profiles { get; set; }=[];
    public AdminPet? Selected { get; set; }
    public AdminService Service { get; set; }=new();
}
internal class AdminProfile
{
    public string Id { get; set; }="";
    public string Name { get; set; }="";
    public bool Paired { get; set; }
    public string? PairingExpiresAtUtc { get; set; }
    public int PendingCount { get; set; }
    public AdminObservation Status { get; set; }=new();
}
internal sealed class AdminPet : AdminProfile
{
    public List<AdminReminder> Reminders { get; set; }=[];
    public List<Prompt> Pending { get; set; }=[];
    public List<AdminActivity> Activity { get; set; }=[];
}
internal sealed class AdminReminder
{
    public string Id { get; set; }="";
    public string Label { get; set; }="";
    public string Time { get; set; }="";
    public string TimeZone { get; set; }="";
    public bool Enabled { get; set; }
    public string NextDueAtUtc { get; set; }="";
}
internal sealed class AdminObservation
{
    public string? LastSeenAtUtc { get; set; }
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
    public string Action { get; set; }="";
    public string Label { get; set; }="";
    public string? Reply { get; set; }
    public int? Minutes { get; set; }
    public string ReceivedAtUtc { get; set; }="";
}
internal sealed record AdminAction
{
    public string Action { get; init; }="";
    public string RequestId { get; init; }=Guid.NewGuid().ToString();
    public string? Pet { get; init; }
    public string? Name { get; init; }
    public string? Label { get; init; }
    public string? Time { get; init; }
    public string? ReminderId { get; init; }
    public string? PromptId { get; init; }
    public string? Text { get; init; }
    public List<string>? Choices { get; init; }
    public bool? AllowReply { get; init; }
}
internal sealed class AdminActionResult
{
    public string Action { get; set; }="";
    public string Pet { get; set; }="";
    public string? Code { get; set; }
    public string? ExpiresAtUtc { get; set; }
}
