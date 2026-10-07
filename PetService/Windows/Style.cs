using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
namespace PetService.Windows;
internal static class Style
{
    internal static readonly Vector4 Accent=new(.76f,.63f,.98f,1),Muted=new(.65f,.67f,.77f,1),Good=new(.48f,.86f,.69f,1);
    internal static void Push()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,new Vector2(22,20));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing,new Vector2(12*Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale,ImGui.GetStyle().ItemInnerSpacing.Y));
        ImGui.PushStyleColor(ImGuiCol.WindowBg,new Vector4(.067f,.075f,.11f,1));ImGui.PushStyleColor(ImGuiCol.ChildBg,new Vector4(.11f,.12f,.17f,1));
        ImGui.PushStyleColor(ImGuiCol.Button,new Vector4(.23f,.19f,.34f,1));ImGui.PushStyleColor(ImGuiCol.ButtonHovered,new Vector4(.35f,.28f,.48f,1));
        ImGui.PushStyleColor(ImGuiCol.Header,new Vector4(.27f,.22f,.39f,1));ImGui.PushStyleColor(ImGuiCol.FrameBg,new Vector4(.085f,.10f,.15f,1));
    }
    internal static void Pop(){ImGui.PopStyleColor(6);ImGui.PopStyleVar(2);}
    internal static void PushContent()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(12,9));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding,8);ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding,12);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,new Vector2(12,10));
    }
    internal static void PopContent()=>ImGui.PopStyleVar(4);
    internal static void Title(string text,string sub)
    {
        ImGui.TextColored(Accent,text);ImGui.TextColored(Muted,sub);ImGui.Spacing();
    }
    internal static bool IconButton(FontAwesomeIcon icon,string text,Vector2 size)
    {
        var width=size.X<0?ImGui.GetContentRegionAvail().X:size.X;
        var textSize=ImGui.CalcTextSize(text,false,Math.Max(1,width-65));size.Y=Math.Max(size.Y,textSize.Y+28);
        var start=ImGui.GetCursorScreenPos();var clicked=ImGui.Button("##"+text,size);
        var draw=ImGui.GetWindowDrawList();ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
        draw.AddText(ImGui.GetFont(),ImGui.GetFontSize(),start+new Vector2(14,14),ImGui.GetColorU32(Accent),icon.ToIconString());ImGui.PopFont();
        draw.AddText(ImGui.GetFont(),ImGui.GetFontSize(),start+new Vector2(46,14),ImGui.GetColorU32(ImGuiCol.Text),text,Math.Max(1,width-65));return clicked;
    }
    internal static void Metric(string label,string value,string id)
    {
        if(ImGui.BeginChild(id,new Vector2(0,82),true)){ImGui.TextColored(Muted,label);ImGui.SetWindowFontScale(1.25f);ImGui.TextUnformatted(value);ImGui.SetWindowFontScale(1);}ImGui.EndChild();
    }
    internal static void DailyChart(IReadOnlyList<Core.DailyTime> days)
    {
        var recent=days.TakeLast(30).ToArray();if(recent.Length==0){ImGui.TextColored(Muted,"No observed playtime yet.");return;}
        var top=ImGui.GetCursorScreenPos();var width=ImGui.GetContentRegionAvail().X;var max=Math.Max(1,recent.Max(d=>d.Seconds));var step=width/recent.Length;
        var draw=ImGui.GetWindowDrawList();
        for(var i=0;i<recent.Length;i++){var h=(float)(recent[i].Seconds/max*95);var x=top.X+i*step;draw.AddRectFilled(new Vector2(x,top.Y+100-h),new Vector2(x+Math.Max(1,step-3),top.Y+100),ImGui.GetColorU32(Accent),3);}
        ImGui.InvisibleButton("##playtimechart",new Vector2(width,106));
        if(ImGui.IsItemHovered()){var i=Math.Clamp((int)((ImGui.GetIO().MousePos.X-top.X)/step),0,recent.Length-1);ImGui.SetTooltip($"{recent[i].Day} · {recent[i].Seconds/3600:F2}h observed");}
        ImGui.TextColored(Muted,$"{recent[0].Day} to {recent[^1].Day} · daily observed playtime");
    }
}
