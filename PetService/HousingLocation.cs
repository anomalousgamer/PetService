using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using PetService.Core;

namespace PetService;

internal static unsafe class HousingLocation
{
    internal static string Observe(uint territoryId, string ordinaryZone)
    {
        var fallback=string.IsNullOrWhiteSpace(ordinaryZone) ? "Unknown" : ordinaryZone;
        try {
            var sheet=Plugin.Data.GetExcelSheet<TerritoryType>();
            var current=sheet?.GetRowOrDefault(territoryId);
            // Housing pointers can remain cached after leaving an estate. Both
            // the live territory and the loaded housing territory must agree.
            if(current is null || current.Value.TerritoryIntendedUse.RowId is not (13 or 14))return fallback;
            var manager=HousingManager.Instance();
            if(manager==null || manager->CurrentTerritory==null || !manager->CurrentTerritory->IsLoaded())return fallback;
            var type=manager->GetCurrentHousingTerritoryType();
            HousingLocationKind kind;
            switch(type) {
                case HousingTerritoryType.Outdoor when current.Value.TerritoryIntendedUse.RowId==13:
                    kind=HousingLocationKind.Outdoors;break;
                case HousingTerritoryType.Indoor when current.Value.TerritoryIntendedUse.RowId==14:
                    kind=HousingLocationKind.Inside;break;
                case HousingTerritoryType.Workshop when current.Value.TerritoryIntendedUse.RowId==14:
                    kind=HousingLocationKind.Workshop;break;
                default:return fallback;
            }
            var ward=manager->GetCurrentWard();
            var plot=manager->GetCurrentPlot();
            var division=manager->GetCurrentDivision();
            var room=manager->GetCurrentRoom();
            var house=manager->GetCurrentHouseId();
            var apartment=(house.Id!=ulong.MaxValue && house.IsApartment) || plot is -128 or -127;
            if(apartment && plot is -128 or -127)division=(byte)(plot==-127 ? 2 : 1);

            // Match the estate's original territory to the outdoor district via
            // PlaceNameZone. Renovated interiors may have a different theme.
            string? District(uint id) {
                var row=sheet?.GetRowOrDefault(id);
                if(row is null)return null;
                if(row.Value.TerritoryIntendedUse.RowId==13)return row.Value.PlaceName.Value.Name.ToString();
                if(row.Value.TerritoryIntendedUse.RowId!=14 || row.Value.PlaceNameZone.RowId==0)return null;
                var zone=row.Value.PlaceNameZone.RowId;
                foreach(var outdoor in sheet!)
                    if(outdoor.TerritoryIntendedUse.RowId==13 && outdoor.PlaceNameZone.RowId==zone)
                        return outdoor.PlaceName.Value.Name.ToString();
                return null;
            }
            var district=house.Id!=ulong.MaxValue ? District(house.TerritoryTypeId) : null;
            if(string.IsNullOrWhiteSpace(district) && kind!=HousingLocationKind.Outdoors)
                district=District(HousingManager.GetOriginalHouseTerritoryTypeId());
            if(string.IsNullOrWhiteSpace(district))district=District(territoryId);
            return HousingLocationText.Format(district ?? fallback,kind,ward,plot,division,room,apartment);
        } catch {
            // An unavailable housing signature must not stop normal status sync.
            return fallback;
        }
    }
}
