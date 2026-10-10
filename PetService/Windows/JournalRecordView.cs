using System.Text.Json;
using Dalamud.Bindings.ImGui;
using PetService.Core;

namespace PetService.Windows;

internal static class JournalRecordView
{
    internal static void Draw(JsonElement record,Action? footer=null)
    {
        var key=PortalJson.Text(record,"key");
        if(key.Length==0)key=PortalJson.Text(record,"kind")+":"+PortalJson.Text(record,"id");
        Style.BeginCard(key,ActivityLabels.Kind(PortalJson.Text(record,"kind")),PortalPresentation.Date(PortalJson.Text(record,"atUtc")));
        var name=PortalJson.Text(record,"characterName");var world=PortalJson.Text(record,"homeWorld");
        if(name.Length>0)ImGui.TextColored(Style.Muted,name+(world.Length>0?" @ "+world:""));
        ImGui.TextWrapped(PortalPresentation.Description(record));
        var data=PortalJson.Get(record,"data");
        if(PortalJson.Text(record,"kind")=="trade")DrawTrade(record);
        if(ImGui.CollapsingHeader("Additional details")&&data.ValueKind==JsonValueKind.Object) {
            foreach(var field in data.EnumerateObject()) {
                if(field.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) {
                    if(ImGui.CollapsingHeader(ActivityLabels.Field(field.Name)))ImGui.TextWrapped(LocalClock.ExportDetails(field.Value));
                } else ImGui.TextWrapped(ActivityLabels.Field(field.Name)+": "+(field.Name.EndsWith("Utc",StringComparison.Ordinal)?PortalPresentation.Date(field.Value.ToString()):ActivityLabels.Value(field.Value)));
            }
        }
        footer?.Invoke();Style.EndCard();
    }
    private static void DrawTrade(JsonElement record)
    {
        var activity=JsonSerializer.Deserialize<ActivityRecord>(record.GetRawText(),ServiceClient.Json);
        if(activity is null)return;
        var display=TradePresentation.Read(activity);
        if(ImGui.BeginTable("contents",2,ImGuiTableFlags.SizingStretchSame|ImGuiTableFlags.BordersInnerV)) {
            ImGui.TableNextColumn();Style.Title(display.Outcome=="Completed"?"Pet gave":"Pet offered to give","");ImGui.TextWrapped(display.GiveItems);ImGui.TextWrapped(display.GiveGil);
            ImGui.TableNextColumn();Style.Title(display.Outcome=="Completed"?"Pet received":"Pet was offered","");ImGui.TextWrapped(display.ReceiveItems);ImGui.TextWrapped(display.ReceiveGil);
            ImGui.EndTable();
        }
        ImGui.TextWrapped(display.Detail);
    }
}
