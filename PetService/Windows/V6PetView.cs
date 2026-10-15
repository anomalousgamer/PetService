using System.Numerics;
using Dalamud.Bindings.ImGui;
using PetService.Core;

namespace PetService.Windows;

internal sealed class V6PetView(Plugin plugin)
{
    private string reply="";
    private string selected="";
    internal bool EditingText{get;private set;}
    internal void Draw()
    {
        EditingText=false;var state=plugin.Configuration.Features.V6;
        Style.Title("Rewards & Punishments","Your balances and recent changes from "+plugin.MasterName);
        if(ImGui.BeginTable("point-balances",2,ImGuiTableFlags.SizingStretchSame)) {
            ImGui.TableNextColumn();Style.Metric("Reward Points",state.Points.Reward.ToString("N0"),"rewards");
            ImGui.TableNextColumn();Style.Metric("Punishment Points",state.Points.Punishment.ToString("N0"),"punishments");ImGui.EndTable();
        }
        ImGui.TextColored(Style.Muted,"Updated "+PortalPresentation.Date(state.Points.UpdatedAtUtc));
        var chat=plugin.Configuration.PointChatNotifications;if(ImGui.Checkbox("Point updates in Master chat",ref chat))plugin.Mutate(c=>c.PointChatNotifications=chat);
        var toast=plugin.Configuration.PointToastNotifications;if(ImGui.Checkbox("Point update toast",ref toast))plugin.Mutate(c=>c.PointToastNotifications=toast);
        Style.Title("Available rewards","Choose a reward to request approval");
        foreach(var reward in state.Rewards.Where(r=>r.Enabled)) {
            Style.BeginCard(reward.Id,reward.Name,reward.Cost+" Reward Points");ImGui.TextWrapped(reward.Description);
            ImGui.ProgressBar(Math.Clamp(state.Points.Reward/(float)Math.Max(1,reward.Cost),0,1),new Vector2(-1,18),state.Points.Reward+" / "+reward.Cost);
            ImGui.BeginDisabled(!plugin.SharingAllowed||state.Points.Reward<reward.Cost||plugin.Configuration.V6Outbox.Any(e=>e.Action=="redeem"&&e.RewardId==reward.Id));
            if(ImGui.Button("Request reward"))plugin.V6.Queue(new(){Action="redeem",RewardId=reward.Id});ImGui.EndDisabled();Style.EndCard();
        }
        if(!state.Rewards.Any(r=>r.Enabled))ImGui.TextColored(Style.Muted,"No rewards listed yet.");
        if(ImGui.CollapsingHeader("Your reward requests"))foreach(var request in state.Redemptions){ImGui.TextWrapped(request.Name+" · "+PortalPresentation.State(request.State)+" · "+request.Cost+" points");if(request.Note.Length>0)ImGui.TextWrapped(request.Note);ImGui.TextColored(Style.Muted,PortalPresentation.Date(request.RequestedAtUtc));}
        if(ImGui.CollapsingHeader("Point history",ImGuiTreeNodeFlags.DefaultOpen))foreach(var entry in state.Points.Entries){ImGui.TextWrapped((entry.Delta>=0?"+":"")+entry.Delta+" "+PortalPresentation.State(entry.Kind)+" Points · "+PortalPresentation.Date(entry.AtUtc));if(entry.Note.Length>0)ImGui.TextWrapped(entry.Note);ImGui.Separator();}
    }
    internal void Routines()
    {
        EditingText=false;var state=plugin.Configuration.Features.V6;Style.Title("Routines","Scheduled reminders and messages from "+plugin.MasterName);
        foreach(var routine in state.Routines.Where(r=>r.Enabled)) {
            Style.BeginCard(routine.Id,routine.Label,routine.Mode=="once"?PortalPresentation.Date(routine.OneOffUtc):string.Join(" · ",routine.Times.Select(t=>LocalClock.ScheduleTime(t,routine.TimeZone,null,plugin.Now)))+" · Your local time");
            if(routine.Mode=="weekdays")ImGui.TextWrapped(string.Join(", ",V6Local.LocalSlots(routine,plugin.Now).Select(s=>new[]{"Sunday","Monday","Tuesday","Wednesday","Thursday","Friday","Saturday"}[s.Weekday]+" "+s.Time)));Style.EndCard();
        }
        var pending=state.Prompts.Where(p=>p.State=="pending").OrderByDescending(p=>p.Policy.Priority).ThenBy(p=>p.SendAtUtc,StringComparer.Ordinal).ToList();
        if(Style.BeginCombo("Pending message or occurrence",pending.FirstOrDefault(p=>p.Id==selected)?.Title??"Select an occurrence")){foreach(var p in pending){ImGui.PushID(p.Id);if(ImGui.Selectable(p.Title+" · "+PortalPresentation.Date(p.SendAtUtc),p.Id==selected)){selected=p.Id;reply="";}ImGui.PopID();}ImGui.EndCombo();}
        if(pending.FirstOrDefault(p=>p.Id==selected) is { } prompt) {
            if(prompt.MissedPolicy=="group"&&prompt.RoutineId is not null){var group=pending.Where(p=>p.RoutineId==prompt.RoutineId).ToList();ImGui.TextWrapped(group.Count+" overdue occurrences in this routine. Each keeps its own response.");foreach(var entry in group)ImGui.TextColored(Style.Muted,PortalPresentation.Date(entry.SendAtUtc));}
            ImGui.TextWrapped(prompt.Text);ImGui.TextColored(Style.Muted,prompt.State);
            ImGui.BeginDisabled(!plugin.SharingAllowed||!V6Local.Due(prompt,plugin.Now));
            foreach(var option in prompt.Responses.Options.Where(o=>o.Action is not("snooze" or "random-snooze" or "open-reply")))if(Style.LiteralButton(option.Label,option.Id,new Vector2(-1,36)))plugin.V6.Queue(new(){PromptId=prompt.Id,Revision=prompt.Revision,Action="response",OptionId=option.Id});
            if(prompt.Responses.AllowReply||prompt.Responses.Options.Any(o=>o.Action=="open-reply")){ImGui.InputTextMultiline("Your reply",ref reply,1000,new Vector2(-1,70));EditingText|=ImGui.IsItemActive();ImGui.BeginDisabled(string.IsNullOrWhiteSpace(reply));if(ImGui.Button("Send reply"))plugin.V6.Queue(new(){PromptId=prompt.Id,Revision=prompt.Revision,Action="reply",Reply=reply});ImGui.EndDisabled();}
            ImGui.EndDisabled();
        }
        if(pending.Count==0)ImGui.TextColored(Style.Muted,"No pending routines or messages.");
    }
}
