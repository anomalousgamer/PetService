using System.Text.Json;
using System.Text.RegularExpressions;
namespace PetService.Core;
internal static class ActivityLabels
{
    internal static string Kind(string kind)=>kind switch {
        "currency"=>"Currency change","combat"=>"Character unconscious / recovery","response"=>"Pet Service response","bond"=>"Bonding outcome","sleep"=>"Sleep outcome",
        "travel"=>"Completed travel","session"=>"Session change","zone"=>"Location change","state"=>"Activity status change",
        "job"=>"Job or level change","duty"=>"Duty event","inventory"=>"Inventory change",
        "loot"=>"Item received","trade"=>"Trade observation","retainer"=>"Retainer result",
        "gap"=>"Observation interrupted","position"=>"Earlier coordinate sample",_=>Field(kind)
    };
    internal static string Field(string field)=>field switch {
        "fromZone"=>"Origin location","fromWorld"=>"Origin world","fromTerritoryId"=>"Origin territory ID","fromMapId"=>"Origin map ID",
        "fromX"=>"Origin X","fromY"=>"Origin Y","toZone"=>"Destination location","toWorld"=>"Destination world",
        "toTerritoryId"=>"Destination territory ID","toMapId"=>"Destination map ID","toX"=>"Destination X","toY"=>"Destination Y",
        "sameZone"=>"Same zone","travelSeconds"=>"Observed travel seconds","aetheryteId"=>"Destination aetheryte ID",
        "zone"=>"Location","world"=>"World","dataCenter"=>"Data Center","territoryId"=>"Territory ID",
        "mapId"=>"Map ID","x"=>"X coordinate","y"=>"Y coordinate","ready"=>"Character ready",
        "inDuty"=>"In a duty","inCombat"=>"In combat","isAfk"=>"Away from keyboard","gameIdle"=>"Game idle",
        "job"=>"Job","level"=>"Level","crafting"=>"Crafting","gathering"=>"Gathering",
        "mounted"=>"Mounted","cutscene"=>"In a cutscene","unconscious"=>"Unconscious",
        "hq"=>"High quality","itemId"=>"Item ID","dutyId"=>"Duty ID","runId"=>"Duty run",
        "taskId"=>"Venture ID","xp"=>"Experience",_=>Humanize(field.EndsWith("Utc",StringComparison.Ordinal)?field[..^3]:field)
    };
    internal static string TravelMethod(string method)=>method switch{""=>"All methods","teleport"=>"Teleport","return"=>"Return","aethernet"=>"Aethernet / aetheryte transfer","area-transfer"=>"Same-area transfer (method unknown)","zone-transition"=>"Zone transition (method unknown)",_=>method};
    private static string Humanize(string field)
    {var text=Regex.Replace(field,"([a-z])([A-Z])","$1 $2");return text.Length>0?char.ToUpperInvariant(text[0])+text[1..]:text;}
    internal static string Value(object? value)=>LocalClock.DisplayValue(value switch {
        true=>"Yes",false=>"No",null=>"Unknown",
        JsonElement e=>e.ValueKind switch{JsonValueKind.True=>"Yes",JsonValueKind.False=>"No",JsonValueKind.Null=>"Unknown",_=>e.ToString()},
        _=>value.ToString()??"Unknown"
    });
}
