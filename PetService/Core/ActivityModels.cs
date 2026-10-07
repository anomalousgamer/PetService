namespace PetService.Core;

public sealed class ActivityRecord
{
    public string Id { get; set; }=Guid.NewGuid().ToString();
    public string Kind { get; set; }="";
    public string AtUtc { get; set; }="";
    public string SessionId { get; set; }="";
    public string CharacterName { get; set; }="";
    public string HomeWorld { get; set; }="";
    [Newtonsoft.Json.JsonConverter(typeof(ActivityDataConverter))]
    public Dictionary<string,object?> Data { get; set; }=[];
}
public sealed class DynamicSettings
{
    public bool GarbleEnabled { get; set; }
    public int GarbleStrength { get; set; }=100;
    public string GarbleStyle { get; set; }="muffled";
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling=Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<string> Contacts { get; set; }=["I'm requesting attention.","I need help.","I am feeling sad.","I love you.","I want you.","I miss you.","Can we spend some time together?","Please check in with me."];
    public List<MessageTemplate> Templates { get; set; }=[];
    internal void NormalizeContacts()=>Contacts=(Contacts??[]).Where(c=>!string.IsNullOrWhiteSpace(c)).Select(c=>c.Trim()).Distinct(StringComparer.Ordinal).ToList();
}
public sealed class MessageTemplate
{
    public string Name { get; set; }="";
    public string Text { get; set; }="";
    public List<string> Choices { get; set; }=[];
    public bool AllowReply { get; set; }=true;
}
internal sealed class AttentionItem
{
    public string Id { get; set; }="";
    public string Text { get; set; }="";
    public string CreatedAtUtc { get; set; }="";
    public string? HandledAtUtc { get; set; }
}
internal sealed class PlaytimeStats
{
    public double TotalSeconds { get; set; }
    public double TodaySeconds { get; set; }
    public double WeekSeconds { get; set; }
    public double MonthSeconds { get; set; }
    public int SessionCount { get; set; }
    public double AverageSessionSeconds { get; set; }
    public double LongestSessionSeconds { get; set; }
    public List<DailyTime> Daily { get; set; }=[];
    public List<TimeGroup> Jobs { get; set; }=[];
    public List<TimeGroup> Zones { get; set; }=[];
    public List<TimeGroup> Characters { get; set; }=[];
}
internal sealed class DailyTime {public string Day {get;set;}="";public double Seconds {get;set;}}
internal sealed class TimeGroup {public string Label {get;set;}="";public double Seconds {get;set;}}
