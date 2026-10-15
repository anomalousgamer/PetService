using System.Numerics;
using Dalamud.Bindings.ImGui;
using PetService.Core;

namespace PetService.Windows;

internal sealed class InteractionEditor
{
    internal string Title="",Text="",Send="",Expires="",TemplateName="";
    internal ResponseOptions Responses=new();
    internal InteractionPolicy Policy=new();
    internal bool EditingText{get;private set;}
    private bool preview;
    private string previewReply="",previewNotice="";
    internal void Load(string title,string text,ResponseOptions responses,InteractionPolicy policy){Title=title;Text=text;Responses=V6Local.Clone(responses);Policy=V6Local.Clone(policy);previewNotice=previewReply="";}
    internal bool Valid=>!string.IsNullOrWhiteSpace(Text)&&(Policy.Delivery=="chat"||Responses.AllowReply&&Responses.ReplyResolves||Responses.Options.Any(o=>o.Action is "complete" or "decline"||o.Action=="response"&&o.Resolves||o.Action=="open-reply"&&Responses.ReplyResolves))&&Responses.Options.All(o=>!string.IsNullOrWhiteSpace(o.Label))&&Policy.SnoozeMin>=1&&Policy.SnoozeMax>=Policy.SnoozeMin&&Policy.SnoozeMax<=1440&&Policy.SnoozePresets.Count<=10&&Policy.SnoozePresets.All(n=>n>=Policy.SnoozeMin&&n<=Policy.SnoozeMax)&&Responses.Options.All(o=>o.Action!="snooze"||o.Minutes is null or 0||o.Minutes>=Policy.SnoozeMin&&o.Minutes<=Policy.SnoozeMax)&&LocalClock.TryInput(Send,out _)&&LocalClock.TryInput(Expires,out _);
    private void Input(string label,ref string text,int max){ImGui.TextUnformatted(label);ImGui.SetNextItemWidth(-12);ImGui.InputText("##"+label,ref text,max);EditingText|=ImGui.IsItemActive();}
    internal void Draw(string masterName,bool routine=false)
    {
        EditingText=false;if(!routine){Input("Title",ref Title,160);ImGui.TextUnformatted("Message / reminder text");ImGui.InputTextMultiline("##interaction-text",ref Text,3000,new Vector2(-1,110));EditingText|=ImGui.IsItemActive();}
        if(Style.BeginCombo("Delivery",Policy.Delivery)){foreach(var mode in new[]{"popup","chat","both"})if(ImGui.Selectable(PortalPresentation.State(mode),mode==Policy.Delivery))Policy.Delivery=mode;ImGui.EndCombo();}
        var priority=Policy.Priority;if(ImGui.SliderInt("Priority",ref priority,0,10))Policy.Priority=priority;
        if(ImGui.CollapsingHeader("Reply buttons",ImGuiTreeNodeFlags.DefaultOpen)) {
            for(var i=0;i<Responses.Options.Count;i++) {
                var option=Responses.Options[i];ImGui.PushID(option.Id);
                var label=option.Label;Input("Button label",ref label,80);option.Label=label;
                var text=option.Text;Input("Returned text / explanation",ref text,1000);option.Text=text;
                if(Style.BeginCombo("Action",PortalPresentation.State(option.Action))){foreach(var action in new[]{"complete","decline","response","open-reply","snooze","random-snooze","keep-open"})if(ImGui.Selectable(PortalPresentation.State(action),option.Action==action)){option.Action=action;option.Resolves=action is "complete" or "decline";option.Minutes=null;}ImGui.EndCombo();}
                if(option.Action=="response"){var resolves=option.Resolves;if(ImGui.Checkbox("Close after this response",ref resolves))option.Resolves=resolves;}
                if(option.Action=="snooze"){var minutes=option.Minutes??0;if(Style.InputInt("Fixed minutes (0 = pet chooses)",ref minutes))option.Minutes=Math.Clamp(minutes,0,1440);}
                if(ImGui.SmallButton("Remove button")){Responses.Options.RemoveAt(i);ImGui.PopID();break;}
                ImGui.SameLine();if(i>0&&ImGui.SmallButton("Move up")){(Responses.Options[i-1],Responses.Options[i])=(Responses.Options[i],Responses.Options[i-1]);ImGui.PopID();break;}
                ImGui.SameLine();if(i<Responses.Options.Count-1&&ImGui.SmallButton("Move down")){(Responses.Options[i+1],Responses.Options[i])=(Responses.Options[i],Responses.Options[i+1]);ImGui.PopID();break;}
                ImGui.Separator();ImGui.PopID();
            }
            if(Responses.Options.Count<32&&ImGui.Button("Add button"))Responses.Options.Add(new());
            var allow=Responses.AllowReply;if(ImGui.Checkbox("Allow written replies",ref allow))Responses.AllowReply=allow;
            var resolve=Responses.ReplyResolves;if(ImGui.Checkbox("Written replies close the occurrence",ref resolve))Responses.ReplyResolves=resolve;
        }
        if(ImGui.CollapsingHeader("Snooze and deferral policies")) {
            var min=Policy.SnoozeMin;var max=Policy.SnoozeMax;var limit=Policy.SnoozeLimit;var overdue=Policy.OverdueNotifyMinutes;
            if(Style.InputInt("Minimum snooze minutes",ref min))Policy.SnoozeMin=min;
            if(Style.InputInt("Maximum snooze minutes",ref max))Policy.SnoozeMax=max;
            if(Style.InputInt("Snooze count limit (0 = unlimited)",ref limit))Policy.SnoozeLimit=Math.Clamp(limit,0,100);
            if(Style.InputInt("Overdue Discord notice interval (0 = off)",ref overdue))Policy.OverdueNotifyMinutes=Math.Clamp(overdue,0,10080);
            var preset=string.Join(',',Policy.SnoozePresets);Input("Snooze presets (comma separated)",ref preset,100);if(preset.Split(',').All(x=>int.TryParse(x.Trim(),out _)))Policy.SnoozePresets=preset.Split(',').Select(x=>int.Parse(x.Trim())).ToList();
            var duty=Policy.DeferDuty;var combat=Policy.DeferCombat;var cutscene=Policy.DeferCutscene;
            if(ImGui.Checkbox("Wait until outside a duty",ref duty))Policy.DeferDuty=duty;
            if(ImGui.Checkbox("Wait until out of combat",ref combat))Policy.DeferCombat=combat;
            if(ImGui.Checkbox("Wait until after a cutscene",ref cutscene))Policy.DeferCutscene=cutscene;
            ImGui.TextWrapped("Daily routines keep their immediate overdue behavior unless you choose a deferral here. Random snooze remains 1–10 minutes and hides its duration from the pet.");
        }
        if(ImGui.Button(preview?"Close local preview":"Preview locally"))preview=!preview;
        if(preview) {
            Style.BeginCard("local-preview","Message from "+masterName,"Local preview · no pet receives this");ImGui.SetWindowFontScale(1.2f);ImGui.TextWrapped(Text);ImGui.SetWindowFontScale(1);
            foreach(var option in Responses.Options)if(Style.LiteralButton(option.Label.Length>0?option.Label:"Empty label",option.Id,new Vector2(-1,34)))previewNotice=option.Action switch {"open-reply"=>"Written reply field opened.","random-snooze"=>"Reminder snoozed.","snooze"=>"Snooze preview; nothing scheduled.","keep-open"=>option.Text,"decline"=>"Declined; this occurrence would close.",_=>option.Resolves?"Response sent; this occurrence would close.":"Response sent; this occurrence would stay open."};
            ImGui.InputTextMultiline("##preview-reply",ref previewReply,1000,new Vector2(-1,60));EditingText|=ImGui.IsItemActive();if(ImGui.Button("Preview written response"))previewNotice=Responses.ReplyResolves?"Written reply would close this occurrence.":"Written reply would leave this occurrence open.";
            ImGui.TextWrapped(previewNotice);Style.EndCard();
        }
    }
    internal void Dates(){Input("Send on your clock (yyyy-MM-dd HH:mm, blank = now)",ref Send,24);Input("Expires on your clock (blank = no expiration)",ref Expires,24);}
}
