namespace PetService.Core;

public sealed class InteractionPolicy
{
    public string Delivery{get;set;}="popup";
    public int Priority{get;set;}
    public int SnoozeMin{get;set;}=1;
    public int SnoozeMax{get;set;}=10;
    public int SnoozeLimit{get;set;}
    public List<int> SnoozePresets{get;set;}=[1,5,10];
    public bool DeferDuty{get;set;}
    public bool DeferCombat{get;set;}
    public bool DeferCutscene{get;set;}
    public int OverdueNotifyMinutes{get;set;}
}
public sealed class WeeklySlot
{
    public int Weekday{get;set;}
    public string Time{get;set;}="";
}
public sealed class Routine
{
    public string Id{get;set;}="";
    public string Revision{get;set;}="";
    public string Label{get;set;}="";
    public bool Enabled{get;set;}=true;
    public string Mode{get;set;}="daily";
    public List<string> Times{get;set;}=[];
    public List<WeeklySlot> WeekSlots{get;set;}=[];
    public List<int> Weekdays{get;set;}=[0,1,2,3,4,5,6];
    public string? OneOffUtc{get;set;}
    public string TimeZone{get;set;}="";
    public string CreatedAtUtc{get;set;}="";
    public string? GeneratedThroughUtc{get;set;}
    public string? EffectiveAtUtc{get;set;}
    public string MissedPolicy{get;set;}="today";
    public int CatchUpDays{get;set;}=7;
    public ResponseOptions Responses{get;set;}=new();
    public InteractionPolicy Policy{get;set;}=new();
}
public sealed class AdvancedPrompt
{
    public string Id{get;set;}="";
    public string Revision{get;set;}="";
    public string? RoutineId{get;set;}
    public string Title{get;set;}="";
    public string Text{get;set;}="";
    public string SendAtUtc{get;set;}="";
    public string? ExpiresAtUtc{get;set;}
    public string CreatedAtUtc{get;set;}="";
    public string State{get;set;}="pending";
    public string? DisplayedAtUtc{get;set;}
    public string? ResolvedAtUtc{get;set;}
    public string? SnoozedUntilUtc{get;set;}
    public int SnoozeCount{get;set;}
    public string MissedPolicy{get;set;}="today";
    public ResponseOptions Responses{get;set;}=new();
    public InteractionPolicy Policy{get;set;}=new();
    public List<InteractionHistory> History{get;set;}=[];
}
public sealed class InteractionHistory
{
    public string State{get;set;}="";
    public string Text{get;set;}="";
    public string AtUtc{get;set;}="";
}
public sealed class AdvancedEvent
{
    public string Id{get;set;}=Guid.NewGuid().ToString();
    public string? PromptId{get;set;}
    public string? Revision{get;set;}
    public string Action{get;set;}="";
    public string? OptionId{get;set;}
    public int? Minutes{get;set;}
    public string? Reply{get;set;}
    public string? RewardId{get;set;}
    public string ClientAtUtc{get;set;}="";
}
public sealed class PointEntry
{
    public string Id{get;set;}="";
    public string Kind{get;set;}="reward";
    public int Delta{get;set;}
    public string Note{get;set;}="";
    public string AtUtc{get;set;}="";
    public int? Balance{get;set;}
    public string Source{get;set;}="";
}
public sealed class PointBalances
{
    public int Reward{get;set;}
    public int Punishment{get;set;}
    public string UpdatedAtUtc{get;set;}="";
    public List<PointEntry> Entries{get;set;}=[];
    public string? NextCursor{get;set;}
}
public sealed class RewardDefinition
{
    public string Id{get;set;}="";
    public string Name{get;set;}="";
    public string Description{get;set;}="";
    public int Cost{get;set;}=1;
    public bool Enabled{get;set;}=true;
}
public sealed class RewardRedemption
{
    public string Id{get;set;}="";
    public string Name{get;set;}="";
    public string Description{get;set;}="";
    public int Cost{get;set;}
    public string State{get;set;}="pending";
    public string RequestedAtUtc{get;set;}="";
    public string? ReviewedAtUtc{get;set;}
    public string Note{get;set;}="";
}
public sealed class InteractionTemplate
{
    public string Id{get;set;}="";
    public string Name{get;set;}="";
    public string Title{get;set;}="";
    public string Text{get;set;}="";
    public ResponseOptions Responses{get;set;}=new();
    public InteractionPolicy Policy{get;set;}=new();
}
public sealed class GagProfile
{
    public string Id{get;set;}="";
    public string Name{get;set;}="";
    public string Style{get;set;}="muffled";
    public int Strength{get;set;}=100;
    public string Syllables{get;set;}="";
    public List<string> ExcludedChannels{get;set;}=[];
    public bool Enabled{get;set;}
    public bool Configured{get;set;}
    public string? ExpiresAtUtc{get;set;}
}
public sealed class TaskPointRule
{
    public string TaskId{get;set;}="";
    public int Reward{get;set;}
    public int PunishmentReduction{get;set;}
}
public sealed class V6State
{
    public List<TaskPointRule> TaskPointRules{get;set;}=[];
    public List<Routine> Routines{get;set;}=[];
    public List<AdvancedPrompt> Prompts{get;set;}=[];
    public PointBalances Points{get;set;}=new();
    public List<RewardDefinition> Rewards{get;set;}=[];
    public List<RewardRedemption> Redemptions{get;set;}=[];
    public List<InteractionTemplate> Templates{get;set;}=[];
    public List<GagProfile> GagProfiles{get;set;}=[];
    public GagProfile Gag{get;set;}=new();
}
