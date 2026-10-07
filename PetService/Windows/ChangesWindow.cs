using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
namespace PetService.Windows;
internal sealed class ChangesWindow(Plugin plugin) : Window("What's new · Pet Service###PetServiceChanges",ImGuiWindowFlags.AlwaysAutoResize)
{
    private bool acknowledged;
    public override void Draw()
    {
        ImGui.PushStyleColor(ImGuiCol.Text,new Vector4(.77f,.64f,1,1));
        ImGui.TextUnformatted("PET SERVICE · 0.3.0.0");ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.TextWrapped("A familiar call opens a door before a whisper leaves.");
        ImGui.BulletText("A voice may find a quieter place to settle.");
        ImGui.BulletText("Returning to the same doorstep still leaves a ribbon behind.");
        ImGui.Spacing();ImGui.TextWrapped("Your kill switch stays yours. Game chat and conversation partners remain private.");
        ImGui.Checkbox("Don't show again for this version",ref acknowledged);
        if(ImGui.Button("Got it")) {
            if(!acknowledged || plugin.Mutate(c=>c.LastAcknowledgedVersion="0.3.0.0"))IsOpen=false;
        }
        ImGui.SameLine();if(ImGui.Button("Check for updates"))Plugin.Commands.ProcessCommand("/toh checkupdates");
    }
}
