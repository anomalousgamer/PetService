using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PetService.Core;

internal static class V6Local
{
    internal static string Id(string text)
    {
        var hex=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }
    internal static List<string> Due(Routine routine,DateTimeOffset now)
    {
        if(!routine.Enabled)return [];
        if(routine.Mode=="once")return routine.OneOffUtc is { } once&&LocalState.Parse(once)<=now?[once]:[];
        var end=DateTime.ParseExact(LocalState.Day(now,routine.TimeZone),"yyyy-MM-dd",CultureInfo.InvariantCulture);
        var days=routine.MissedPolicy=="today"?1:Math.Clamp(routine.CatchUpDays,1,31);var due=new List<string>();
        for(var offset=days-1;offset>=0;offset--) {
            var day=end.AddDays(-offset);
            if(routine.Mode=="weekdays"&&!routine.Weekdays.Contains((int)day.DayOfWeek))continue;
            foreach(var time in routine.Mode=="weekdays"&&routine.WeekSlots.Count>0?routine.WeekSlots.Where(s=>s.Weekday==(int)day.DayOfWeek).Select(s=>s.Time):routine.Times) {
                var stamp=LocalState.DueAt(day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),time,routine.TimeZone);
                if(LocalState.Parse(stamp)>=LocalState.Parse(routine.EffectiveAtUtc??routine.CreatedAtUtc)&&LocalState.Parse(stamp)<=now)due.Add(stamp);
            }
        }
        return due;
    }
    internal static List<WeeklySlot> LocalSlots(Routine routine,DateTimeOffset now,TimeZoneInfo? viewer=null)
    {
        var source=DateTime.ParseExact(LocalState.Day(now,routine.TimeZone),"yyyy-MM-dd",CultureInfo.InvariantCulture);source=source.AddDays(-(int)source.DayOfWeek);
        var slots=routine.WeekSlots.Count>0?routine.WeekSlots:routine.Weekdays.SelectMany(d=>routine.Times.Select(t=>new WeeklySlot{Weekday=d,Time=t})).ToList();
        return slots.Select(s=>{var utc=LocalState.Parse(LocalState.DueAt(source.AddDays(s.Weekday).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),s.Time,routine.TimeZone));var local=TimeZoneInfo.ConvertTime(utc,viewer??TimeZoneInfo.Local);return new WeeklySlot{Weekday=(int)local.DayOfWeek,Time=local.ToString("HH:mm",CultureInfo.InvariantCulture)};}).DistinctBy(s=>s.Weekday+"|"+s.Time).OrderBy(s=>s.Weekday).ThenBy(s=>s.Time).ToList();
    }
    internal static void Refresh(V6State state,DateTimeOffset now)
    {
        foreach(var routine in state.Routines)foreach(var due in Due(routine,now)) {
            var id=Id(routine.Id+"|"+due);if(state.Prompts.Any(p=>p.Id==id)||routine.GeneratedThroughUtc is { } generated&&string.CompareOrdinal(due,generated)<=0)continue;
            state.Prompts.Add(new(){Id=id,Revision=routine.Revision,RoutineId=routine.Id,Title=routine.Label,Text=routine.Label,SendAtUtc=due,CreatedAtUtc=LocalState.Stamp(now),MissedPolicy=routine.MissedPolicy,Responses=Clone(routine.Responses),Policy=Clone(routine.Policy)});
        }
    }
    internal static T Clone<T>(T value)=>System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value,ServiceClient.Json),ServiceClient.Json)!;
    internal static void Merge(V6State remote,V6State local,IEnumerable<AdvancedEvent> pending,DateTimeOffset now)
    {
        foreach(var p in local.Prompts.Where(p=>p.RoutineId is not null&&remote.Routines.Any(r=>r.Id==p.RoutineId&&r.Enabled&&r.Revision==p.Revision&&(r.GeneratedThroughUtc is null||string.CompareOrdinal(p.SendAtUtc,r.GeneratedThroughUtc)>0))))if(!remote.Prompts.Any(r=>r.Id==p.Id))remote.Prompts.Add(p);
        Refresh(remote,now);
        foreach(var e in pending)if(remote.Prompts.FirstOrDefault(p=>p.Id==e.PromptId&&p.Revision==e.Revision) is { } prompt)Apply(prompt,e);
    }
    internal static void Apply(AdvancedPrompt prompt,AdvancedEvent e)
    {
        if(e.Action=="displayed"){prompt.DisplayedAtUtc??=e.ClientAtUtc;if(prompt.Policy.Delivery=="chat"&&!prompt.Responses.AllowReply&&prompt.Responses.Options.Count==0)prompt.State="displayed";}
        else if(e.Action=="snooze") {prompt.SnoozeCount++;prompt.SnoozedUntilUtc=LocalState.Stamp(LocalState.Parse(e.ClientAtUtc).AddMinutes(e.Minutes??1));}
        else if(e.Action=="reply"&&prompt.Responses.ReplyResolves){prompt.State="completed";prompt.ResolvedAtUtc=e.ClientAtUtc;}
        else if(e.Action=="response"&&prompt.Responses.Options.FirstOrDefault(o=>o.Id==e.OptionId) is {Resolves:true} option){prompt.State=option.Action=="decline"?"declined":"completed";prompt.ResolvedAtUtc=e.ClientAtUtc;}
    }
    internal static bool Due(AdvancedPrompt p,DateTimeOffset now)=>p.State=="pending"&&LocalState.Parse(p.SendAtUtc)<=now&&(p.ExpiresAtUtc is null||LocalState.Parse(p.ExpiresAtUtc)>now)&&(p.SnoozedUntilUtc is null||LocalState.Parse(p.SnoozedUntilUtc)<=now);
    internal static bool Deferred(AdvancedPrompt p,Observation status)=>p.Policy.DeferDuty&&status.InDuty||p.Policy.DeferCombat&&status.InCombat||p.Policy.DeferCutscene&&status.Cutscene;
    internal static AdvancedPrompt? Pending(V6State state,DateTimeOffset now,Observation status)=>state.Prompts.Where(p=>Due(p,now)&&p.Policy.Delivery!="chat"&&!Deferred(p,status)).OrderByDescending(p=>p.Policy.Priority).ThenBy(p=>p.SendAtUtc,StringComparer.Ordinal).ThenBy(p=>p.Id,StringComparer.Ordinal).FirstOrDefault();
}
