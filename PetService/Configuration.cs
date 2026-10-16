using Dalamud.Configuration;
using PetService.Core;
namespace PetService;
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public Dictionary<string,string> PrivateChatVaults{get;set;}=[];
    public bool AudioMigrated{get;set;}
    public List<string> SoundPromptReturns{get;set;}=[];
    public List<AudioEvent> AudioEvents{get;set;}=[];
    public List<AudioResult> AudioResults{get;set;}=[];
    public List<string> SeenSoundRequests{get;set;}=[];
    public float RemoteSoundVolume{get;set;}=0.5f;
    public Vector2Chat ChatWindow{get;set;}=new();
    public bool PrivateChatToastNotifications{get;set;}=true;
    public bool PrivateChatLogNotifications{get;set;}=true;
    public ChatLoginRole PrivateChatLoginRole{get;set;}=ChatLoginRole.Off;
    public List<AdvancedEvent> V6Outbox{get;set;}=[];
    public HashSet<string> V6ChatIds{get;set;}=[];
    public HashSet<string> PointNoticeIds{get;set;}=[];
    public HashSet<string> RedemptionNoticeIds{get;set;}=[];
    public bool PointChatNotifications{get;set;}=true;
    public bool PointToastNotifications{get;set;}=true;

    public List<string> EndedSleepIds {get;set;}=[];
    public string MasterChatRgb {get;set;}="";
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

[Serializable]public sealed class Vector2Chat{public float Width{get;set;}=780;public float Height{get;set;}=650;public float Composer{get;set;}=120;}
