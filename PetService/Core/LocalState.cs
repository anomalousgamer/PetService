using System.Globalization;

namespace PetService.Core;
public static class LocalState
{
    private static readonly Dictionary<string, string> DueCache = [];
    public static string Stamp(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    public static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    public static string Day(DateTimeOffset utc, string timeZone) => TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string DueAt(string day, string time, string zone)
    {
        var key = day + "|" + time + "|" + zone;
        if (DueCache.TryGetValue(key, out var result)) return result;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(zone);
        var local = DateTime.SpecifyKind(DateTime.ParseExact(day + " " + time, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), DateTimeKind.Unspecified);
        // Same DST rule as the service: first repeated time, next valid missing time.
        var limit = local.AddDays(1);
        while (tz.IsInvalidTime(local) && local < limit) local = local.AddMinutes(1);
        var utc = tz.IsAmbiguousTime(local)
            ? new DateTimeOffset(local, tz.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime()
            : new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, tz));
        result = Stamp(utc);
        if (DueCache.Count > 1000) DueCache.Clear();
        DueCache[key] = result;
        return result;
    }
    public static bool Refresh(List<Schedule> schedules, List<Prompt> prompts, DateTimeOffset now)
    {
        var changed = false;
        foreach (var r in schedules.Where(r => r.Enabled))
        {
            var day = Day(now, r.TimeZone);
            var due = DueAt(day, r.Time, r.TimeZone);
            var id = $"r:{r.Id}:{day}";
            if (Parse(due) > now || prompts.Any(p => p.Id == id)) continue;
            prompts.Add(new Prompt { Id=id, Kind="reminder", ReminderId=r.Id, LocalDay=day, Label=r.Label, DueAtUtc=due });
            changed = true;
        }
        return changed;
    }
    public static Prompt? Pending(List<Prompt> prompts, DateTimeOffset now) => prompts
        .Where(p => p.ResolvedAtUtc is null && Parse(p.DueAtUtc) <= now && (p.SnoozedUntilUtc is null || Parse(p.SnoozedUntilUtc) <= now))
        .OrderBy(p => p.DueAtUtc, StringComparer.Ordinal).ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
    public static void Apply(Prompt prompt, Choice choice)
    {
        if (choice.Action == "displayed") prompt.DisplayedAtUtc ??= choice.ClientAtUtc;
        else if (choice.Action == "snooze" && prompt.ResolvedAtUtc is null && SnoozePolicy.IsValid(choice.Minutes))
            prompt.SnoozedUntilUtc = Stamp(Parse(choice.ClientAtUtc).AddMinutes(choice.Minutes!.Value));
        else if (choice.Action is "taken" or "acknowledge" or "reply")
        { prompt.ResolvedAtUtc=choice.ClientAtUtc; prompt.Resolution=choice.Action; prompt.SnoozedUntilUtc=null; }
    }
    public static List<Prompt> Merge(List<Prompt> local, SyncResult server, List<Choice> remaining)
    {
        var merged=server.Occurrences.ToDictionary(p=>p.Id, StringComparer.Ordinal);
        foreach (var p in local)
            if (!merged.ContainsKey(p.Id) && p.Kind=="reminder" && server.Reminders.Any(r=>r.Id==p.ReminderId && r.Enabled)) merged[p.Id]=p;
        foreach (var e in remaining)
            if (merged.TryGetValue(e.OccurrenceId, out var p)) Apply(p,e);
        return merged.Values.Where(p=>p.ResolvedAtUtc is null || Parse(p.ResolvedAtUtc)>=Parse(server.ServerTimeUtc).AddDays(-40)).ToList();
    }
}
