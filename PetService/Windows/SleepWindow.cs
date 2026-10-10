using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace PetService.Windows;
internal sealed class SleepWindow:Window
{
    private readonly Plugin plugin;
    internal SleepWindow(Plugin plugin):base("Sleep##PetServiceSleep",ImGuiWindowFlags.NoTitleBar|ImGuiWindowFlags.NoResize|ImGuiWindowFlags.NoMove|ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.AlwaysAutoResize){this.plugin=plugin;IsOpen=true;ShowCloseButton=false;RespectCloseHotkey=false;AllowClickthrough=false;AllowPinning=false;ForceMainWindow=true;DisableWindowSounds=true;DisableFadeInFadeOut=true;}
    public override bool DrawConditions()=>plugin.Sleeping;
    public override void PreDraw(){IsOpen=true;var viewport=ImGui.GetMainViewport();ImGui.SetNextWindowPos(viewport.WorkPos+new Vector2(viewport.WorkSize.X-24,24),ImGuiCond.Always,new Vector2(1,0));}
    public override void Draw(){ImGui.TextColored(Style.Accent,"Sleep");if(ImGui.Button("Emergency Wake Up",new Vector2(190,36)))plugin.EmergencyWake();if(ImGui.Button("Kill switch",new Vector2(190,28)))plugin.SetEnabled(false);}
}
