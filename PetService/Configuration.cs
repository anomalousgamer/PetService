using Dalamud.Configuration;
using PetService.Core;
namespace PetService;
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    // Binds the saved credential to its issuing service. Requests use the bundled URL.
    public string ServiceUrl { get; set; } = "";
    public string DeviceToken { get; set; } = "";
    public string PetId { get; set; } = "";
    public string PetName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool Revoked { get; set; }
    public double ClockOffsetMilliseconds { get; set; }
    public string? LastSyncAtUtc { get; set; }
    public List<Schedule> Reminders { get; set; } = [];
    public List<Prompt> Prompts { get; set; } = [];
    public List<Choice> Outbox { get; set; } = [];
    public string LastAcknowledgedVersion { get; set; }="";
    public DynamicSettings Dynamic { get; set; }=new();
    public List<ActivityRecord> ActivityOutbox { get; set; }=[];
    public List<Choice> SafetyOutbox { get; set; } = [];
}
