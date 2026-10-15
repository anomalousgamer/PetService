using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace PetService.Windows;

internal sealed class PointToastWindow(Plugin plugin):Window("Point update###PetServicePointsToast",ImGuiWindowFlags.NoTitleBar|ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.AlwaysAutoResize|ImGuiWindowFlags.NoFocusOnAppearing|ImGuiWindowFlags.NoMove)
{
    public override bool DrawConditions()=>plugin.SharingAllowed&&plugin.V6.Toast.Length>0&&Environment.TickCount64<plugin.V6.ToastUntil;
    public override void PreDraw(){IsOpen=true;ShowCloseButton=false;RespectCloseHotkey=false;AllowPinning=false;AllowClickthrough=false;var v=ImGui.GetMainViewport();Position=v.Pos+new Vector2(v.Size.X-420,55);PositionCondition=ImGuiCond.Always;ImGui.SetNextWindowSizeConstraints(new(280,0),new(400,float.MaxValue));}
    public override void Draw(){ImGui.TextColored(Style.Accent,"From "+plugin.MasterName);ImGui.PushTextWrapPos(ImGui.GetCursorPosX()+360);ImGui.TextWrapped(plugin.V6.Toast);ImGui.PopTextWrapPos();}
}
