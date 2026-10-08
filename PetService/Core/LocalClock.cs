using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PetService.Core;

internal static class LocalClock
{
    internal static string ZoneId
    {
        get
        {
            var id = TimeZoneInfo.Local.Id;
            return TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) ? iana : id;
        }
    }

    internal static string Input(string? utc, TimeZoneInfo? viewer = null) =>
        DateTimeOffset.TryParse(utc, out var instant)
            ? TimeZoneInfo.ConvertTime(instant, viewer ?? TimeZoneInfo.Local).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "";

    internal static bool TryInput(string text, out string? utc, TimeZoneInfo? viewer = null)
    {
        utc = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!DateTime.TryParseExact(text.Trim(), ["yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return false;
        try
        {
            // Use the same first repeated / next valid missing minute rule as daily schedules.
            utc = LocalState.DueAt(local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                local.ToString("HH:mm", CultureInfo.InvariantCulture), (viewer ?? TimeZoneInfo.Local).Id);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException) { return false; }
    }

    internal static string ScheduleTime(string time, string scheduleZone, string? nextDueUtc,
        DateTimeOffset now, TimeZoneInfo? viewer = null)
    {
        try
        {
            var due = DateTimeOffset.TryParse(nextDueUtc, out var next) ? next :
                LocalState.Parse(LocalState.DueAt(LocalState.Day(now, scheduleZone), time, scheduleZone));
            return TimeZoneInfo.ConvertTime(due, viewer ?? TimeZoneInfo.Local).ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or TimeZoneNotFoundException or InvalidTimeZoneException) { return ""; }
    }

    internal static string Export(string utc) => DateTimeOffset.TryParse(utc, out var instant)
        ? instant.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture) : "";

    internal static string DisplayValue(string value) => Regex.IsMatch(value, @"\A\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z\z")
        && DateTimeOffset.TryParse(value, out var instant) ? instant.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : value;

    internal static string ExportDetails(object details)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(details));
        Convert(node);
        return node?.ToJsonString() ?? "{}";
        static void Convert(JsonNode? node)
        {
            if (node is JsonArray array) foreach (var item in array) Convert(item);
            if (node is not JsonObject obj) return;
            foreach (var (key, value) in obj.ToList())
            {
                if (key.EndsWith("Utc", StringComparison.Ordinal) && value is JsonValue scalar
                    && scalar.TryGetValue<string>(out var stamp) && DateTimeOffset.TryParse(stamp, out _))
                {
                    obj.Remove(key); obj[key[..^3] + "Local"] = Export(stamp);
                }
                else Convert(value);
            }
        }
    }
}
