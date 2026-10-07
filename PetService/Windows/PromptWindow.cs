using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using PetService.Core;

namespace PetService.Windows;

internal sealed class PromptWindow : Window
{
    private readonly Plugin plugin;
    private string? lastPromptId;
    private bool wasVisible,editingPrompt,mouseOverChat,drawFailed;
    private long appearedAt;
    private string reply="";
    private int snoozeMinutes=SnoozePolicy.DefaultMinutes;
    private const ImGuiWindowFlags PromptFlags=ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings
        | ImGuiWindowFlags.NoFocusOnAppearing;

    public PromptWindow(Plugin plugin) : base("Pet Service###PetServicePrompt")
    {
        this.plugin=plugin;
        IsOpen=true;ShowCloseButton=false;RespectCloseHotkey=false;
        AllowClickthrough=false;AllowPinning=false;ForceMainWindow=true;
        InhibitAtkCollision=true;DisableWindowSounds=true;DisableFadeInFadeOut=true;
        Flags=PromptFlags;
    }
    public override bool DrawConditions()
    {
        if(plugin.HasVisibleReminder)return true;
        wasVisible=false;editingPrompt=false;return false;
    }
    public override void PreDraw()
    {
        var viewport=ImGui.GetMainViewport();
        var chat=plugin.ChatState;
        var mouse=ImGui.GetIO().MousePos-viewport.Pos;
        mouseOverChat=chat.Available && (chat.InputArea.Contains(mouse) || chat.LogArea.Contains(mouse));
        if(mouseOverChat && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
            ImGuiP.ClearActiveID();editingPrompt=false;
        }
        var prompt=plugin.CurrentPrompt;
        if(!wasVisible || prompt?.Id!=lastPromptId) {
            appearedAt=Environment.TickCount64;reply="";snoozeMinutes=SnoozePolicy.DefaultMinutes;
            lastPromptId=prompt?.Id;editingPrompt=false;
            // Keep existing game-chat focus. A prompt appearing must not take it.
        }
        wasVisible=true;
        var scale=ImGuiHelpers.GlobalScale;
        var size=Vector2.Min(new Vector2(580,(prompt?.Kind=="reminder" ? 360 : 540))*scale,
            Vector2.Max(new Vector2(1),viewport.Size-new Vector2(32)*scale));
        var position=viewport.Pos+(viewport.Size-size)/2f;
        if(chat.Available && Overlap(position-viewport.Pos,size,chat.InputArea)>0) {
            var gap=8*scale;
            var above=Math.Clamp(chat.InputArea.Minimum.Y-gap,0,viewport.Size.Y);
            var bottom=Math.Clamp(chat.InputArea.Maximum.Y+gap,0,viewport.Size.Y);
            var below=viewport.Size.Y-bottom;
            var left=Math.Clamp(chat.InputArea.Minimum.X-gap,0,viewport.Size.X);
            var right=Math.Clamp(chat.InputArea.Maximum.X+gap,0,viewport.Size.X);
            if(above>=size.Y)position.Y=viewport.Pos.Y+above-size.Y;
            else if(below>=size.Y)position.Y=viewport.Pos.Y+bottom;
            else if(left>=size.X)position.X=viewport.Pos.X+left-size.X;
            else if(viewport.Size.X-right>=size.X)position.X=viewport.Pos.X+right;
            else if(Math.Max(above,below)>=260*scale) {
                // Scroll the contents in a shorter window when the chat input
                // is placed near the middle of a smaller game viewport.
                size.Y=Math.Min(size.Y,Math.Max(above,below));
                position.Y=viewport.Pos.Y+(above>=below ? above-size.Y : bottom);
            }
        }
        Position=position;PositionCondition=ImGuiCond.Always;
        Size=size;SizeCondition=ImGuiCond.Always;
        Flags=PromptFlags | (drawFailed ? ImGuiWindowFlags.NoInputs : 0);
        InhibitAtkCollision=!drawFailed;
        if(!drawFailed)plugin.CaptureInput(editingPrompt,mouseOverChat);
    }
    public override void Draw()
    {
        try {DrawPrompt();}
        catch {drawFailed=true;plugin.ReleaseInput();throw;}
    }
    private void DrawPrompt()
    {
        var prompt=plugin.CurrentPrompt;
        if(prompt is null)return;
        var scale=ImGuiHelpers.GlobalScale;
        plugin.MarkDisplayed();
        ImGui.SetWindowFontScale(1.2f);
        ImGui.TextUnformatted(prompt.Kind=="reminder" ? "Daily medication reminder" : "Message from Master");
        ImGui.SetWindowFontScale(1f);ImGui.Separator();
        var armed=Environment.TickCount64-appearedAt>=500;
        editingPrompt=false;
        if(ImGui.BeginChild("Prompt contents",new Vector2(-1,-124*scale),false)) {
            try {
                ImGui.SetWindowFontScale(1.35f);ImGui.PushTextWrapPos(0);
                ImGui.TextUnformatted(prompt.Kind=="reminder" ? prompt.Label : prompt.Text);
                ImGui.PopTextWrapPos();ImGui.SetWindowFontScale(1f);ImGui.Spacing();
                if(prompt.Kind=="message") {
                    var options=prompt.Choices ?? [];
                    for(var i=0;i<Math.Min(options.Count,32);i++) {
                        ImGui.PushID(i);ImGui.BeginDisabled(!armed);
                        // Draw the label literally, including any ImGui ## markers.
                        var width=ImGui.GetContentRegionAvail().X;
                        var labelSize=ImGui.CalcTextSize(options[i],false,width-16*scale);
                        var start=ImGui.GetCursorScreenPos();
                        var clicked=ImGui.Button("##choice",new Vector2(width,Math.Max(32*scale,labelSize.Y+12*scale)));
                        ImGui.GetWindowDrawList().AddText(ImGui.GetFont(),ImGui.GetFontSize(),start+new Vector2(8,6)*scale,
                            ImGui.GetColorU32(ImGuiCol.Text),options[i],width-16*scale);
                        ImGui.EndDisabled();ImGui.PopID();
                        if(clicked)plugin.Choose("choice",optionIndex:i);
                    }
                    if(prompt.AllowReply) {
                        ImGui.TextUnformatted("Your reply");
                        ImGui.InputTextMultiline("##PromptReply",ref reply,1000,new Vector2(-1,70*scale));
                        editingPrompt=ImGui.IsItemActive();
                        ImGui.BeginDisabled(!armed || string.IsNullOrWhiteSpace(reply));
                        if(ImGui.Button("Send reply",new Vector2(-1,32*scale)))plugin.Choose("reply",reply);
                        ImGui.EndDisabled();
                    }
                }
                ImGui.Spacing();ImGui.SetNextItemWidth(125*scale);
                ImGui.InputInt("Snooze minutes",ref snoozeMinutes,1,5);
                editingPrompt|=ImGui.IsItemActive();
                var valid=SnoozePolicy.IsValid(snoozeMinutes);
                if(!valid)ImGui.TextUnformatted("Choose 1 to 1,440 minutes.");
                ImGui.BeginDisabled(!armed || !valid);
                if(ImGui.Button($"Snooze {snoozeMinutes} minutes",new Vector2(-1,32*scale)))plugin.Choose("snooze",minutes:snoozeMinutes);
                ImGui.EndDisabled();
                if(prompt.Kind=="reminder") {
                    ImGui.BeginDisabled(!armed);
                    if(ImGui.Button("I took them",new Vector2(-1,32*scale)))plugin.Choose("taken");
                    ImGui.EndDisabled();
                }
            } finally {ImGui.SetWindowFontScale(1f);ImGui.EndChild();}
        } else ImGui.EndChild();
        ImGui.Separator();
        if(ImGui.Button("Use game chat",new Vector2(-1,28*scale))) {
            ImGuiP.ClearActiveID();editingPrompt=false;plugin.FocusGameChat();
        }
        if(ImGui.Button("Contact master",new Vector2(-1,28*scale)))plugin.OpenContact();
        if(ImGui.Button("Kill switch — stop master input",new Vector2(-1,28*scale)))plugin.SetEnabled(false);
        if(!string.IsNullOrEmpty(plugin.SaveError))ImGui.TextWrapped(plugin.SaveError);
        if(plugin.HasVisibleReminder)plugin.CaptureInput(editingPrompt,mouseOverChat);
    }
    private static float Overlap(Vector2 position,Vector2 size,ChatArea area)
    {
        var extent=Vector2.Min(position+size,area.Maximum)-Vector2.Max(position,area.Minimum);
        return MathF.Max(0,extent.X)*MathF.Max(0,extent.Y);
    }
}
