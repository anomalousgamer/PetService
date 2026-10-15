using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using PetService.Core;

namespace PetService.Windows;

internal sealed class ChangesWindow : Window
{
    private readonly Plugin plugin;
    private bool acknowledged;

    internal ChangesWindow(Plugin plugin)
        : base("What's new · Pet Service###PetServiceChanges")
    {
        this.plugin = plugin;
        Size = new Vector2(680, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new()
        {
            MinimumSize = new Vector2(450, 330),
            MaximumSize = new Vector2(float.MaxValue),
        };
    }

    public override void Draw()
    {
        var accent = new Vector4(.77f, .64f, 1, 1);
        ImGui.TextColored(accent, "PET SERVICE · " + ReleaseNotes.Version);
        ImGui.Separator();

        // Keep the footer outside the scrolling body, even for long notes.
        var footerHeight = 2 * ImGui.GetFrameHeightWithSpacing()
            + ImGui.GetStyle().ItemSpacing.Y + 4 * ImGuiHelpers.GlobalScale;
        var bodyHeight = Math.Max(60 * ImGuiHelpers.GlobalScale,
            ImGui.GetContentRegionAvail().Y - footerHeight);
        var visible = ImGui.BeginChild("release-notes", new Vector2(0, bodyHeight), false);
        try
        {
            if (visible)
            {
                foreach (var section in ReleaseNotes.Sections)
                {
                    if (!string.IsNullOrEmpty(section.Title))
                    {
                        ImGui.Spacing();
                        ImGui.TextColored(accent, section.Title);
                        ImGui.Separator();
                    }
                    foreach (var riddle in section.Riddles)
                    {
                        ImGui.TextWrapped(riddle);
                        ImGui.Spacing();
                    }
                }
            }
        }
        finally
        {
            ImGui.EndChild();
        }

        ImGui.Separator();
        ImGui.Checkbox("Don't show again for this version", ref acknowledged);
        if (ImGui.Button("Got it")
            && (!acknowledged || plugin.Mutate(c => c.LastAcknowledgedVersion = ReleaseNotes.Version)))
            IsOpen = false;
        ImGui.SameLine();
        if (ImGui.Button("Check for updates"))
            Plugin.Commands.ProcessCommand("/toh checkupdates");
    }
}
