using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace PetService.Windows;

internal sealed class SettingsWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string code = "";
    private readonly MasterPortalView master=new();
    internal bool EditingText { get; private set; }

    public SettingsWindow(Plugin plugin) : base("Pet Service")
    {
        this.plugin = plugin;
        SizeConstraints = new() { MinimumSize = new Vector2(480, 320), MaximumSize = new Vector2(float.MaxValue) };
        Size = new Vector2(700, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        EditingText=false;
        if(ImGui.BeginTabBar("PetServicePages")) {
            if(ImGui.BeginTabItem("Pet")){DrawPet();ImGui.EndTabItem();}
            if(ImGui.BeginTabItem("Master")) {
                master.Draw();EditingText=master.EditingText;
                ImGui.Separator();var paused=plugin.KillSwitchOn;
                if(ImGui.Checkbox("Local kill switch — stop master input on this device",ref paused))plugin.SetEnabled(!paused);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }
    private void DrawPet()
    {
        if (plugin.IsPaired)
            ImGui.TextUnformatted("Paired profile: " + plugin.Configuration.PetName);
        else
            ImGui.TextUnformatted("Setup");
        ImGui.TextWrapped(plugin.ServiceStatus);
        ImGui.Separator();
        ImGui.BeginDisabled(!plugin.CanCallMaster);
        if(ImGui.Button("Call master"))plugin.RequestAttention();
        ImGui.EndDisabled();
        if(plugin.CallCooldownSeconds>0)ImGui.TextUnformatted($"Call available in {plugin.CallCooldownSeconds}s.");

        var paused = plugin.KillSwitchOn;
        if (ImGui.Checkbox("Kill switch — stop all master input", ref paused))
            plugin.SetEnabled(!paused);
        paused = plugin.KillSwitchOn;
        ImGui.TextWrapped(paused
            ? "ON: master input and status/location sharing are paused. A kill-switch notice is queued for the master. Stays on until you turn it off."
            : "OFF: the service can send reminders and messages and receive your status/location.");

        if (!plugin.IsPaired)
        {
            ImGui.Spacing();
            ImGui.InputText("One-time pairing code", ref code, 80, ImGuiInputTextFlags.Password);
            EditingText|=ImGui.IsItemActive();
            ImGui.BeginDisabled(plugin.NetworkBusy || string.IsNullOrWhiteSpace(code));
            if (ImGui.Button("Pair")) { plugin.Pair(code); code = ""; }
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
    internal void ClearTextFocus(){EditingText=false;master.ClearTextFocus();}
    public override void OnClose(){code="";EditingText=false;master.Lock();}
    public void Dispose()=>master.Dispose();
}
