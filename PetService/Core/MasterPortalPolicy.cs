using System.Globalization;
using System.Text.RegularExpressions;

namespace PetService.Core;

internal static class MasterPortalPolicy
{
    internal static bool ValidName(string name)=>Regex.IsMatch(name,@"\A[a-z0-9][a-z0-9_-]{0,31}\z");
    internal static bool ValidTime(string time)=>time.Length==5 && TimeOnly.TryParseExact(time,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _);
    internal static string? ChoiceError(string lines,bool allowReply,out List<string> labels)
    {
        labels=lines.Split('\n').Select(x=>x.Trim()).Where(x=>x.Length>0).ToList();
        if(labels.Count>32 || labels.Any(x=>x.Length>80 || x.Any(char.IsControl)) || labels.Distinct(StringComparer.Ordinal).Count()!=labels.Count)
            return "Use up to 32 different button labels, one per line, with at most 80 characters each.";
        return labels.Count==0 && !allowReply ? "Add a reply button or allow a written reply." : null;
    }
    internal static bool Fresh(AdminProfile pet,DateTimeOffset now)=>pet.Status.Reported is not null
        && DateTimeOffset.TryParse(pet.Status.LastSeenAtUtc,out var seen) && (now-seen).TotalSeconds<=45;
    internal static string State(AdminProfile pet,DateTimeOffset now)=>!pet.Paired ? "Not paired"
        : pet.Status.Reported is null ? "Awaiting first connection" : !Fresh(pet,now) ? "No recent contact"
        : pet.Status.Reported.LoggedIn ? "Logged in" : "Logged out";
}
