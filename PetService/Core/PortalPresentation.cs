using System.Globalization;
using System.Text.Json;

namespace PetService.Core;

internal static class PortalJson
{
    internal static JsonElement Get(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var result) ? result : default;
    internal static string Text(JsonElement value, string key) => String(Get(value, key));
    internal static string String(JsonElement value) => value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? "" : value.ToString();
    internal static bool Bool(JsonElement value, string key) => Get(value, key).ValueKind == JsonValueKind.True;
    internal static double Number(JsonElement value, string key) => Get(value, key) is { ValueKind: JsonValueKind.Number } number && number.TryGetDouble(out var result) ? result : 0;
    internal static List<JsonElement> Array(JsonElement value, string key) =>
        Get(value, key) is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray().ToList() : [];
}

internal static class PortalPresentation
{
    internal static readonly (string Category, string[] Pages)[] Navigation = [
        ("Now", ["Overview", "Inventory", "Tasks"]),
        ("Control", ["Bonding", "Messages", "Chat messages", "Reminders", "Integrations"]),
        ("History", ["Journal", "Analytics", "Zone visits", "Duty summaries", "Trade summary", "Travel", "Inventory history", "Retainers", "Bonding history", "Task evidence"]),
        ("Manage", ["Settings", "Master identity", "Rewards", "Profile"])
    ];
    internal static string State(string value) => value switch {
        "cancelled" => "Canceled", "lock" => "Bonding", "random-snooze" => "Random snooze",
        _ => ActivityLabels.Field(value.Replace('-', ' ').Replace('_', ' '))
    };
    internal static string Date(string? value) => DateTimeOffset.TryParse(value, out var instant)
        ? instant.ToLocalTime().ToString("MMM d, yyyy · h:mm tt", CultureInfo.GetCultureInfo("en-US")) : "Not observed";
    internal static string Count(double value) => value.ToString("N0", CultureInfo.GetCultureInfo("en-US"));
    internal static string Description(JsonElement record)
    {
        var d = PortalJson.Get(record, "data");
        string Text(string key) => PortalJson.Text(d, key);
        string Join(params string[] values) => string.Join(" · ", values.Where(v => v.Length > 0));
        var kind = PortalJson.Text(record, "kind");
        return kind switch {
            "bond" or "sleep" or "honorific" or "moodles" or "task" => Join(State(Text("operation")), State(Text("state")), Text("detail")),
            "response" => Join(State(Text("action")), Text("optionLabel"), Text("reply"), Text("request"), PortalJson.Number(d, "minutes") > 0 ? "Snoozed " + Count(PortalJson.Number(d, "minutes")) + " minutes" : ""),
            "zone" => Join(Text("world"), Text("zone"), Coordinates(d, "x", "y")),
            "job" => Join(Text("job"), Text("level").Length > 0 ? "Level " + Text("level") : ""),
            "duty" => Join(Text("name"), State(Text("phase")), Text("outcome") is "" or "unconfirmed" || Text("outcome") == Text("phase") ? "" : State(Text("outcome"))),
            "inventory" => State(Text("operation")) + ": " + (Text("name").Length > 0 ? Text("name") : "Item " + Text("itemId")) + (PortalJson.Bool(d, "hq") ? " HQ" : "") + " × " + Text("quantity") + " · " + Text("container") + " · Slot " + Text("slot") + " · Source unconfirmed",
            "travel" => ActivityLabels.TravelMethod(Text("method")) + ": " + Join(Text("fromWorld"), Text("fromZone")) + " → " + Join(Text("toWorld"), Text("toZone")) + (PortalJson.Bool(d, "sameZone") ? " · Same-zone arrival" : ""),
            "session" => Join(Text("source") == "end" ? "Recording stopped" : "Session observed", Text("reason")),
            "gap" => Join("Recording interrupted", Text("reason")),
            "state" => StateDescription(d),
            "currency" => (Text("currency").Length > 0 ? Text("currency") : "Currency") + " changed by " + (PortalJson.Number(d, "delta") > 0 ? "+" : "") + Count(PortalJson.Number(d, "delta")) + " · Balance " + Count(PortalJson.Number(d, "amount")) + " · Source unconfirmed",
            "combat" => State(Text("phase")),
            "retainer" => Join(Text("name"), string.Join(", ", PortalJson.Array(d, "items").Select(Item)), State(Text("phase"))),
            "trade" => State(Text("outcome")) + " · Gave " + Count(PortalJson.Number(d, "giveGil")) + " gil · Received " + Count(PortalJson.Number(d, "receiveGil")) + " gil",
            _ => "Saved " + ActivityLabels.Kind(kind).ToLowerInvariant() + "."
        };
    }
    internal static string Coordinates(JsonElement data, string x, string y) =>
        PortalJson.Get(data, x).ValueKind == JsonValueKind.Number && PortalJson.Get(data, y).ValueKind == JsonValueKind.Number
            ? "X " + PortalJson.Number(data, x).ToString("0.00", CultureInfo.InvariantCulture) + ", Y " + PortalJson.Number(data, y).ToString("0.00", CultureInfo.InvariantCulture) : "";
    internal static string Item(JsonElement item) => (PortalJson.Text(item, "name") is { Length: > 0 } name ? name : "Item " + PortalJson.Text(item, "itemId")) + (PortalJson.Bool(item, "hq") ? " HQ" : "") + " × " + Count(PortalJson.Number(item, "quantity"));
    private static string StateDescription(JsonElement data)
    {
        var states = new (string Key, string Label)[] { ("inDuty", "In a duty"), ("inCombat", "In combat"), ("isAfk", "Away from keyboard"), ("gameIdle", "Idle"), ("crafting", "Crafting"), ("gathering", "Gathering"), ("mounted", "Mounted"), ("cutscene", "Watching a cutscene"), ("unconscious", "Unconscious") };
        var active = states.Where(s => PortalJson.Bool(data, s.Key)).Select(s => s.Label).ToList();
        return active.Count > 0 ? string.Join(" · ", active) : PortalJson.Get(data, "ready").ValueKind == JsonValueKind.False ? "Character not ready" : "No active duty, combat, crafting, or gathering reported.";
    }
}

internal static class IntegrationCatalog
{
    internal static JsonElement Find(FeatureState features, string character) =>
        features.Catalogs.FirstOrDefault(c => PortalJson.Text(c, "characterId") == character) is { ValueKind: JsonValueKind.Object } found ? found :
        features.Catalog is { ValueKind: JsonValueKind.Object } fallback && PortalJson.Text(fallback, "characterId") == character ? fallback : default;
    internal static string TitleKey(JsonElement title) => JsonSerializer.Serialize(new object[] { PortalJson.Text(title, "title"), PortalJson.Bool(title, "prefix"), PortalJson.Text(title, "colour"), PortalJson.Text(title, "glow") });
    internal static string TitleLabel(JsonElement title) => PortalJson.Text(title, "title") + (PortalJson.Bool(title, "prefix") ? " · Before name" : " · After name") + (PortalJson.Text(title, "colour") is { Length: > 0 } color ? " · " + color.ToUpperInvariant() : "");
    internal static bool SafeStatus(JsonElement status) => status.ValueKind == JsonValueKind.Object && PortalJson.Text(status, "id").Length > 0 && PortalJson.Bool(status, "removable");
    internal static bool SafePreset(JsonElement preset, JsonElement moodles)
    {
        var ids = PortalJson.Array(preset, "statuses");
        var statuses = PortalJson.Array(moodles, "statuses");
        return ids.Count > 0 && ids.All(id => statuses.Any(status => PortalJson.Text(status, "id") == PortalJson.String(id) && SafeStatus(status)));
    }
    internal static HashSet<string> Managed(FeatureState features, JsonElement catalog)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var commands = features.ActiveIntegrationCommands ?? features.IntegrationCommands;
        foreach (var command in commands.Where(c => c.Kind == "moodles" && c.State == "applied" && c.CharacterId == PortalJson.Text(catalog, "characterId"))) {
            if (command.StatusId.Length > 0) result.Add(command.StatusId);
            var preset = PortalJson.Array(PortalJson.Get(catalog, "moodles"), "presets").FirstOrDefault(p => PortalJson.Text(p, "id") == command.Preset);
            foreach (var id in PortalJson.Array(preset, "statuses")) result.Add(PortalJson.String(id));
        }
        return result;
    }
    internal static bool Minutes(int minutes) => minutes is >= 0 and <= 10080;
}
