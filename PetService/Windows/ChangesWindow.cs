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
        ImGui.TextUnformatted("PET SERVICE · 0.4.0.1");ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.TextWrapped("Master fucking broke some shit.\n\nFrom v0.4.0.0\nA new face may greet you at the threshold.\nEach door remembers whose hand may turn its key.\nSmall promises find a place to wait.\nA quiet pause need not carry a letter.");
        ImGui.Spacing();
        ImGui.Checkbox("Don't show again for this version",ref acknowledged);
        if(ImGui.Button("Got it")) {
            if(!acknowledged || plugin.Mutate(c=>c.LastAcknowledgedVersion="0.4.0.1"))IsOpen=false;
        }
        ImGui.SameLine();if(ImGui.Button("Check for updates"))Plugin.Commands.ProcessCommand("/toh checkupdates");
    }
}
