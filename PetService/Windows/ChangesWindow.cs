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
        ImGui.TextUnformatted("PET SERVICE · 0.5.0.0");ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.TextWrapped("A soft word may lay the day to rest; a small light always knows the way back.\nOld footsteps gather into pages, and every voice may choose its own shade.");
        ImGui.Spacing();
        ImGui.Checkbox("Don't show again for this version",ref acknowledged);
        if(ImGui.Button("Got it")) {
            if(!acknowledged || plugin.Mutate(c=>c.LastAcknowledgedVersion="0.5.0.0"))IsOpen=false;
        }
        ImGui.SameLine();if(ImGui.Button("Check for updates"))Plugin.Commands.ProcessCommand("/toh checkupdates");
    }
}
