namespace PetService.Core;

internal enum HousingLocationKind { Outdoors, Inside, Workshop }

internal static class HousingLocationText
{
    internal static string Format(string district, HousingLocationKind kind, int wardIndex,
        int plotIndex, int division, int room, bool apartment)
    {
        // Native ward and plot values are zero based. Negative plot values are
        // sentinels, not estate numbers; 60 plots include both ward divisions.
        var parts=new List<string>();
        if(wardIndex is >=0 and <64)parts.Add($"W{wardIndex+1}");
        var plotKnown=!apartment && plotIndex is >=0 and <60;
        if(plotKnown)parts.Add($"P{plotIndex+1}");
        if(division==2)parts.Add("Subdivision");
        if(apartment) {
            if(kind==HousingLocationKind.Outdoors)parts.Add("Apartment building grounds");
            else if(room is >0 and <1023)parts.Add($"Apartment {room} · Inside apartment");
            else parts.Add("Apartment lobby");
        } else if(kind==HousingLocationKind.Workshop)parts.Add("Workshop");
        else if(kind==HousingLocationKind.Inside) {
            if(room is >0 and <1023)parts.Add($"FC room {room}");
            parts.Add("Inside house");
        } else parts.Add(plotKnown ? "Estate grounds" : "Ward outdoors");

        var suffix=" · "+string.Join(" · ",parts);
        var name=string.IsNullOrWhiteSpace(district) ? "Housing district" : district.Trim();
        // The existing service accepts at most 160 characters for the location.
        // Preserve the address and indoor/outdoor state if a localized name is long.
        var limit=160-suffix.Length;
        if(name.Length>limit) {
            var take=limit-1;
            if(take>0 && char.IsHighSurrogate(name[take-1]))take--;
            name=name[..take]+"…";
        }
        return name+suffix;
    }
}
