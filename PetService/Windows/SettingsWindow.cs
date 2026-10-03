using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace PetService.Windows;

internal sealed class SettingsWindow : Window
{
    private readonly Plugin plugin;
    private string url, code = "";

    public SettingsWindow(Plugin plugin) : base("Pet Service setup")
    {
        this.plugin = plugin;
        url = plugin.Configuration.ServiceUrl;
        SizeConstraints = new() { MinimumSize = new Vector2(440, 240), MaximumSize = new Vector2(float.MaxValue) };
        Size = new Vector2(480, 310);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (plugin.IsPaired)
            ImGui.TextUnformatted("Paired profile: " + plugin.Configuration.PetName);
        else
            ImGui.TextUnformatted("Setup");
        ImGui.TextWrapped(plugin.ServiceStatus);
        ImGui.Separator();

        var paused = plugin.KillSwitchOn;
        if (ImGui.Checkbox("Kill switch — stop all master input", ref paused))
            plugin.SetEnabled(!paused);
        paused = plugin.KillSwitchOn;
        ImGui.TextWrapped(paused
            ? "ON: no reminders, master messages, or status/location sharing. Stays on until you turn it off."
            : "OFF: the service can send reminders and messages and receive your status/location.");

        if (!plugin.IsPaired)
        {
            ImGui.Spacing();
            ImGui.InputText("HTTPS service URL", ref url, 400);
            ImGui.InputText("One-time pairing code", ref code, 80, ImGuiInputTextFlags.Password);
            ImGui.BeginDisabled(plugin.NetworkBusy);
            if (ImGui.Button("Pair")) { plugin.Pair(url, code); code = ""; }
            ImGui.EndDisabled();
            ImGui.TextWrapped("Setup is saved for future logins. This shares character status, zone/world/DC, and explicit popup replies with the master, including on alts. Ordinary game chat is not collected.");
        }
        else
        {
            ImGui.Spacing();
            ImGui.BeginDisabled(plugin.NetworkBusy);
            if (ImGui.Button("Unpair…")) ImGui.OpenPopup("Unpair this device?");
            ImGui.EndDisabled();
        }

        var confirmationOpen = true;
        if (ImGui.BeginPopupModal("Unpair this device?", ref confirmationOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("Clear local pairing, cached reminders, and unsent replies?");
            ImGui.TextUnformatted("Request a new pairing code if you want to reconnect.");
            ImGui.BeginDisabled(plugin.NetworkBusy);
            if (ImGui.Button("Unpair")) { plugin.Unpair(); ImGui.CloseCurrentPopup(); }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        if (!string.IsNullOrEmpty(plugin.SaveError)) ImGui.TextWrapped(plugin.SaveError);
        if (!plugin.KillSwitchOn && !string.IsNullOrEmpty(plugin.ScheduleError)) ImGui.TextWrapped(plugin.ScheduleError);
    }
}
