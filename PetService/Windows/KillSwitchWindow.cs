using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace PetService.Windows;

internal sealed class KillSwitchWindow : Window
{
    private const string PopupName="Turn on kill switch?###PetServicePauseConfirmation";
    private readonly Plugin plugin;
    private bool openRequested;
    public KillSwitchWindow(Plugin plugin) : base("###PetServiceConfirmationHost",
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings)
    {
        this.plugin=plugin;
        IsOpen=false;
        ForceMainWindow=true;
        DisableFadeInFadeOut=true;
        ShowCloseButton=false;
        RespectCloseHotkey=false;
        Size=new Vector2(1);
        SizeCondition=ImGuiCond.Always;
    }
    internal void ShowConfirmation() {openRequested=true;IsOpen=true;}
    public override void Draw()
    {
        if(openRequested) {ImGui.OpenPopup(PopupName);openRequested=false;}
        var viewport=ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos+viewport.Size/2f,ImGuiCond.Appearing,new Vector2(.5f));
        var keepOpen=true;
        if(ImGui.BeginPopupModal(PopupName,ref keepOpen,ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings)) {
            ImGui.TextUnformatted("Pause all master messages, reminders, and status sharing?");
            ImGui.TextUnformatted("A kill-switch notice will be sent to the master.");
            ImGui.TextUnformatted("Only you can turn it off again.");
            ImGui.Spacing();
            if(ImGui.Button("Confirm — turn on kill switch")) {plugin.ConfirmKillSwitch();ImGui.CloseCurrentPopup();IsOpen=false;}
            ImGui.SameLine();
            if(ImGui.Button("Cancel")) {ImGui.CloseCurrentPopup();IsOpen=false;}
            ImGui.EndPopup();
        }
        if(!keepOpen || !ImGui.IsPopupOpen(PopupName))IsOpen=false;
    }
}
