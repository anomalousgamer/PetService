using System.Globalization;

namespace PetService.Core;

internal sealed record CalendarOccurrence(string Id,string Label,DateTimeOffset AtUtc,string Kind,string State);
internal sealed class CalendarMonth
{
    public List<CalendarOccurrence> Entries {get;}=[];
    public List<string> Unavailable {get;}=[];
}

// A projection of the cached schedule. Browsing never changes a routine or creates an occurrence.
internal static class RoutineCalendar
{
    internal static CalendarMonth Build(IEnumerable<Routine> routines,IEnumerable<Schedule> reminders,
        IEnumerable<AdvancedPrompt> routinePrompts,IEnumerable<Prompt> reminderPrompts,
        DateTime month,TimeZoneInfo viewer,DateTimeOffset now)
    {
        var result=new CalendarMonth();
        var first=new DateTime(month.Year,month.Month,1);var last=first.AddMonths(1);
        var from=LocalState.Parse(LocalState.DueAt(first.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),"00:00",viewer.Id));
        var until=LocalState.Parse(LocalState.DueAt(last.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),"00:00",viewer.Id));
        var entries=new Dictionary<string,CalendarOccurrence>(StringComparer.Ordinal);
        bool Within(DateTimeOffset at)=>at>=from&&at<until;
        void Add(string id,string label,DateTimeOffset at,string kind,string state="Scheduled")
        {
            if(Within(at))entries[id]=new(id,label,at.ToUniversalTime(),kind,state);
        }
        void Read(string label,Action action)
        {
            try{action();}
            catch(Exception e) when(e is ArgumentException or FormatException or TimeZoneNotFoundException or InvalidTimeZoneException)
            {result.Unavailable.Add(label);}
        }
        void Days(string zone,Action<DateTime> visit)
        {
            var tz=TimeZoneInfo.FindSystemTimeZoneById(zone);
            var start=TimeZoneInfo.ConvertTime(from,tz).Date.AddDays(-1);
            var end=TimeZoneInfo.ConvertTime(until,tz).Date.AddDays(1);
            for(var day=start;day<=end;day=day.AddDays(1))visit(day);
        }
        foreach(var routine in routines.Where(r=>r.Enabled))Read(routine.Label,()=>{
            if(routine.Mode=="once"){
                if(routine.OneOffUtc is { } one)Add(V6Local.Id(routine.Id+"|"+one),routine.Label,LocalState.Parse(one),"One-time reminder");
                return;
            }
            var effective=DateTimeOffset.TryParse(routine.EffectiveAtUtc??routine.CreatedAtUtc,out var boundary)?boundary:DateTimeOffset.MinValue;
            Days(routine.TimeZone,day=>{
                var weekday=(int)day.DayOfWeek;
                if(routine.Mode=="weekdays"&&!routine.Weekdays.Contains(weekday))return;
                var times=routine.Mode=="weekdays"&&routine.WeekSlots.Count>0
                    ?routine.WeekSlots.Where(s=>s.Weekday==weekday).Select(s=>s.Time):routine.Times;
                foreach(var time in times){var stamp=LocalState.DueAt(day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),time,routine.TimeZone);var at=LocalState.Parse(stamp);
                    if(at>=effective)Add(V6Local.Id(routine.Id+"|"+stamp),routine.Label,at,routine.Mode=="weekdays"?"Weekday routine":"Daily routine");}
            });
        });
        foreach(var reminder in reminders.Where(r=>r.Enabled))Read(reminder.Label,()=>{
            var created=DateTimeOffset.TryParse(reminder.CreatedAtUtc,out var boundary)?boundary:DateTimeOffset.MinValue;
            Days(reminder.TimeZone,day=>{var date=day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);var at=LocalState.Parse(LocalState.DueAt(date,reminder.Time,reminder.TimeZone));
                if(at>=created)Add($"r:{reminder.Id}:{date}",reminder.Label,at,"Daily reminder");});
        });
        string State(string state,string? snoozed,string? resolved)
        {
            if(resolved is not null||state!="pending")return state switch{"completed"=>"Completed","declined"=>"Declined","cancelled"=>"Canceled","expired"=>"Expired","displayed"=>"Displayed",_=>"Acknowledged"};
            if(DateTimeOffset.TryParse(snoozed,out var snooze)&&snooze>now)return "Snoozed";
            return "Pending";
        }
        foreach(var prompt in routinePrompts.Where(p=>p.RoutineId is not null))Read(prompt.Title,()=>{
            var at=LocalState.Parse(prompt.SendAtUtc);Add(prompt.Id,prompt.Title,at,"Routine occurrence",at>now&&prompt.State=="pending"?"Scheduled":State(prompt.State,prompt.SnoozedUntilUtc,prompt.ResolvedAtUtc));
        });
        foreach(var prompt in reminderPrompts.Where(p=>p.Kind=="reminder"))Read(prompt.Label,()=>{
            var at=LocalState.Parse(prompt.DueAtUtc);Add(prompt.Id,prompt.Label,at,"Daily reminder",at>now&&prompt.ResolvedAtUtc is null?"Scheduled":State(prompt.ResolvedAtUtc is null?"pending":prompt.Resolution switch{"cancelled"=>"cancelled","declined"=>"declined","expired"=>"expired",_=>"acknowledged"},prompt.SnoozedUntilUtc,prompt.ResolvedAtUtc));
        });
        result.Entries.AddRange(entries.Values.OrderBy(e=>e.AtUtc).ThenBy(e=>e.Label,StringComparer.Ordinal).ThenBy(e=>e.Id,StringComparer.Ordinal));
        return result;
    }
}
