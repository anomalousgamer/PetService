using Dalamud.Configuration;
using PetService.Core;
namespace PetService;
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public List<CharacterAccess> Characters {get;set;}=[];
    public Dictionary<string,string> ManagedTitles {get;set;}=[];
    public Dictionary<string,string> PreviousTitles {get;set;}=[];
    public Dictionary<string,List<Guid>> ManagedMoodles {get;set;}=[];
    public List<IntegrationLease> IntegrationLeases {get;set;}=[];
    public List<string> IntegrationCleanupCharacters {get;set;}=[];
    public bool CharacterAccessMigrated {get;set;}
    public FeatureState Features {get;set;}=new();
    public List<FeatureResult> FeatureResults {get;set;}=[];
    public List<TaskEvent> TaskEvents {get;set;}=[];
    public List<RecoveryRecord> Recovery {get;set;}=[];
    public string QueuesPetId {get;set;}="";
    public int Version { get; set; } = 1;
    // Binds the saved credential to its issuing service. Requests use the bundled URL.
    public string ServiceUrl { get; set; } = "";
    public string DeviceToken { get; set; } = "";
    public string PetId { get; set; } = "";
    public string PetName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public long AccessRevision {get;set;}
    public bool Revoked { get; set; }
    public bool RevocationConfirmed {get;set;}
    public double ClockOffsetMilliseconds { get; set; }
    public string? LastSyncAtUtc { get; set; }
    public List<Schedule> Reminders { get; set; } = [];
    public List<Prompt> Prompts { get; set; } = [];
    public List<Choice> Outbox { get; set; } = [];
    public string LastAcknowledgedVersion { get; set; }="";
    public string LastLoadedVersion {get;set;}="";
    public int ChatColourRevision {get;set;}
    public DynamicSettings Dynamic { get; set; }=new();
    public List<ActivityRecord> ActivityOutbox { get; set; }=[];
    public List<Choice> SafetyOutbox { get; set; } = [];
    public string MasterChatType {get;set;}="echo";
    public ushort MasterChatColour {get;set;}=0;
    public bool MasterChatSound {get;set;}
    public List<string> DisplayedChatIds {get;set;}=[];
}
