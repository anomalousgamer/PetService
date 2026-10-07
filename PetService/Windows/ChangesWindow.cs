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
        ImGui.TextUnformatted("PET SERVICE · 0.2.0.0");ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.TextWrapped("A familiar door opens into a warmer room.");
        ImGui.BulletText("Eight small whispers now know the way home.");
        ImGui.BulletText("Footsteps leave a trail, but private words leave none.");
        ImGui.BulletText("Some voices may find themselves wrapped in velvet.");
        ImGui.BulletText("An old companion answers to a shorter name: /toh.");
        ImGui.Spacing();ImGui.TextWrapped("Your kill switch stays yours. Setup explains what the plugin shares; game chat and conversation partners remain private.");
        ImGui.Checkbox("Don't show again for this version",ref acknowledged);
        if(ImGui.Button("Got it")) {
            if(!acknowledged || plugin.Mutate(c=>c.LastAcknowledgedVersion="0.2.0.0"))IsOpen=false;
        }
        ImGui.SameLine();if(ImGui.Button("Check for updates"))Plugin.Commands.ProcessCommand("/toh checkupdates");
    }
}
