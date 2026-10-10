using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
namespace PetService.Windows;
internal static class Style
{
    private static readonly Stack<(Vector2 Start,float Width)> Cards=[];
    internal static Vector4 Accent=new(.76f,.63f,.98f,1),Muted=new(.74f,.76f,.85f,1),Good=new(.48f,.86f,.69f,1);
    internal static Vector4 AccentText => Accent.X*299+Accent.Y*587+Accent.Z*114 < 145*1000/255f ? Vector4.One : new(.09f,.07f,.13f,1);
    internal static void ApplyTheme(string value){if(value.Length==7&&value[0]=='#'&&uint.TryParse(value.AsSpan(1),System.Globalization.NumberStyles.HexNumber,null,out var n))Accent=new(((n>>16)&255)/255f,((n>>8)&255)/255f,(n&255)/255f,1);}
    internal static void BeginCard(string id,string title="",string description="")
    {
        ImGui.PushID(id);ImGui.BeginGroup();
        Cards.Push((ImGui.GetCursorScreenPos(),ImGui.GetContentRegionAvail().X));
        ImGui.Dummy(new Vector2(0,12));ImGui.Indent(12);ImGui.PushItemWidth(Math.Max(1,ImGui.GetContentRegionAvail().X-12));
        if(title.Length>0)Title(title,description);
    }
    internal static void EndCard()
    {
        var card=Cards.Pop();ImGui.PopItemWidth();ImGui.Unindent(12);
        ImGui.Dummy(new Vector2(card.Width,12));ImGui.EndGroup();
        ImGui.GetWindowDrawList().AddRect(card.Start,new Vector2(card.Start.X+card.Width,ImGui.GetItemRectMax().Y),ImGui.GetColorU32(new Vector4(.23f,.25f,.33f,1)),12);
        ImGui.PopID();ImGui.Spacing();
    }
    internal static bool ColorField(string label,ref string hex)
    {
        var rgb=0xffffffu;if(hex.Length==7&&hex[0]=='#')uint.TryParse(hex.AsSpan(1),System.Globalization.NumberStyles.HexNumber,null,out rgb);
        var color=new Vector3(((rgb>>16)&255)/255f,((rgb>>8)&255)/255f,(rgb&255)/255f);
        ImGui.PushID(label);ImGui.TextUnformatted(label);
        if(ImGui.ColorButton("##color",new Vector4(color,1),ImGuiColorEditFlags.NoTooltip,new Vector2(74,44)))ImGui.OpenPopup("Choose color");
        ImGui.SameLine();ImGui.TextUnformatted(hex.ToUpperInvariant());
        var changed=false;
        if(ImGui.BeginPopup("Choose color")) {
            ImGui.SetNextItemWidth(280*Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale);
            if(ImGui.ColorPicker3("##picker",ref color,ImGuiColorEditFlags.PickerHueWheel|ImGuiColorEditFlags.DisplayHex)) {
                hex=$"#{Math.Clamp((int)Math.Round(color.X*255),0,255):x2}{Math.Clamp((int)Math.Round(color.Y*255),0,255):x2}{Math.Clamp((int)Math.Round(color.Z*255),0,255):x2}";changed=true;
            }
            if(ImGui.Button("Done"))ImGui.CloseCurrentPopup();ImGui.EndPopup();
        }
        ImGui.PopID();return changed;
    }
    internal static bool PrimaryButton(string label,Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button,Accent);ImGui.PushStyleColor(ImGuiCol.Text,AccentText);
        var clicked=ImGui.Button(label,size);ImGui.PopStyleColor(2);return clicked;
    }
    internal static bool BeginCombo(string label,string preview)
    {
        ImGui.TextUnformatted(label);ImGui.SetNextItemWidth(-12);
        return ImGui.BeginCombo("##"+label,preview);
    }
    internal static bool InputInt(string label,ref int value)
    {
        ImGui.TextWrapped(label);ImGui.SetNextItemWidth(Math.Max(1,Math.Min(220,ImGui.GetContentRegionAvail().X-12)));
        return ImGui.InputInt("##"+label,ref value);
    }
    internal static bool Combo(string label,ref int value,string[] choices,int count)
    {
        ImGui.TextUnformatted(label);ImGui.SetNextItemWidth(-12);return ImGui.Combo("##"+label,ref value,choices,count);
    }
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
        ImGui.PushStyleColor(ImGuiCol.Text,Accent);ImGui.TextWrapped(text);ImGui.PopStyleColor();
        if(sub.Length>0){ImGui.PushStyleColor(ImGuiCol.Text,Muted);ImGui.TextWrapped(sub);ImGui.PopStyleColor();}ImGui.Spacing();
    }
    internal static bool LiteralButton(string text,string id,Vector2 size){var start=ImGui.GetCursorScreenPos();var width=size.X<0?ImGui.GetContentRegionAvail().X:size.X;var height=Math.Max(size.Y,ImGui.CalcTextSize(text,false,width-16).Y+16);var clicked=ImGui.Button("##"+id,new Vector2(width,height));ImGui.GetWindowDrawList().AddText(ImGui.GetFont(),ImGui.GetFontSize(),start+new Vector2(8,8),ImGui.GetColorU32(ImGuiCol.Text),text,width-16);return clicked;}
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
        var padding=ImGui.GetStyle().WindowPadding;
        var width=Math.Max(1,ImGui.GetContentRegionAvail().X-padding.X*2-8);
        var height=Math.Max(92*Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale,
            padding.Y*2+ImGui.CalcTextSize(label,false,width).Y+ImGui.GetStyle().ItemSpacing.Y+ImGui.CalcTextSize(value,false,width/1.25f).Y*1.25f+8);
        if(ImGui.BeginChild(id,new Vector2(0,height),true,ImGuiWindowFlags.NoScrollbar|ImGuiWindowFlags.NoScrollWithMouse)) {
            ImGui.TextColored(Muted,label);ImGui.SetWindowFontScale(1.25f);ImGui.TextWrapped(value);ImGui.SetWindowFontScale(1);
        }ImGui.EndChild();
    }
    internal static void StateGroup(string id,string title,bool fresh,IReadOnlyList<(string Label,string Value,bool Active)> rows,bool known=true)
    {
        ImGui.SetWindowFontScale(1.12f);
        ImGui.TextColored(Accent,title);ImGui.Spacing();
        if(ImGui.BeginTable(id,2,ImGuiTableFlags.SizingStretchProp|ImGuiTableFlags.RowBg|ImGuiTableFlags.BordersInnerH)) {
            ImGui.TableSetupColumn("Status",ImGuiTableColumnFlags.WidthStretch,1.4f);
            ImGui.TableSetupColumn("State",ImGuiTableColumnFlags.WidthStretch,1);
            foreach(var row in rows) {
                ImGui.TableNextRow();ImGui.TableNextColumn();ImGui.TextWrapped(row.Label);ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text,known&&fresh&&row.Active?Good:Muted);ImGui.TextWrapped(known?(fresh?"":"Last: ")+row.Value:"Unknown");ImGui.PopStyleColor();
            }
            ImGui.EndTable();
        }
        ImGui.SetWindowFontScale(1);
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
