using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using PetService.Core;

namespace PetService.Windows;

internal sealed class V6MasterView
{
    private string context="",routineId="",routineLabel="",times="18:00",weeklyTimes="1 18:00",once="",mode="daily",missed="today",promptId="",templateId="",profileId="",profileName="",profileStyle="muffled",syllables="",gagPreview="A little message for you.",rewardId="",rewardName="",rewardDescription="",pointKind="reward",pointNote="",reviewNote="",taskId="";
    private bool routineEnabled=true,rewardEnabled=true;
    private readonly bool[] weekdays=[true,true,true,true,true,true,true];
    private readonly HashSet<string> excluded=[];
    private int catchUp=7,strength=100,gagMinutes,delta=10,rewardCost=10,taskReward,taskReduction;
    private readonly InteractionEditor routineEditor=new(),promptEditor=new();
    private InteractionEditor editor=>activePage=="Routines"?routineEditor:promptEditor;
    private string activePage="";
    private readonly List<PointEntry> olderPoints=[];
    private string? pointCursor;
    internal bool EditingText{get;private set;}
    private void Input(string label,ref string value,int max=1000){ImGui.TextUnformatted(label);ImGui.SetNextItemWidth(-12);ImGui.InputText("##"+label,ref value,max);EditingText|=ImGui.IsItemActive();}
    private static void Send(MasterPortalSession session,string action,object data)=>session.Action(new(){Action=action,Pet=session.Selected,Data=data});
    internal void Clear(){context="";routineId=promptId=templateId=profileId=rewardId=taskId="";olderPoints.Clear();pointCursor=null;routineEditor.Load("","",new(),new());promptEditor.Load("","",new(),new());routineLabel=once=profileName=syllables=rewardName=rewardDescription=pointNote=reviewNote="";excluded.Clear();mode="daily";times="18:00";weeklyTimes="1 18:00";missed="today";catchUp=7;strength=100;gagMinutes=0;profileStyle="muffled";pointKind="reward";delta=10;rewardCost=10;taskReward=taskReduction=0;routineEnabled=rewardEnabled=true;}
    internal void Begin(MasterPortalSession session){EditingText=false;if(context==session.Selected)return;Clear();context=session.Selected;}
    internal void Completed(AdminAction action,AdminActionResult result)
    {
        if(action.Action=="pointsHistory"&&result.Data is {ValueKind:JsonValueKind.Object} data){var page=JsonSerializer.Deserialize<PointBalances>(data.GetRawText(),ServiceClient.Json);if(page is not null){olderPoints.AddRange(page.Entries);pointCursor=page.NextCursor;}}
    }
    internal void Draw(MasterPortalSession session,AdminPet pet,string page)
    {
        Begin(session);activePage=page;
        switch(page){case "Routines":Routines(session,pet);break;case "Scheduled messages":Prompts(session,pet);break;case "Gag profiles":Gags(session,pet);break;case "Points":Points(session,pet);break;}
        EditingText|=editor.EditingText||ImGui.IsAnyItemActive();
    }
    private void Routines(MasterPortalSession session,AdminPet pet)
    {
        var state=pet.Features.V6;Style.Title("Routine calendar","Several daily times, selected weekdays, or one-time reminders");
        if(ImGui.CollapsingHeader("Next seven days",ImGuiTreeNodeFlags.DefaultOpen)) {
            var slots=new List<(DateTimeOffset At,string Label)>();
            foreach(var r in state.Routines.Where(r=>r.Enabled)) {
                if(r.Mode=="once"&&r.OneOffUtc is { } one){var at=LocalState.Parse(one);if(at>=session.Now&&at<session.Now.AddDays(7))slots.Add((at,r.Label));continue;}
                for(var offset=-1;offset<=8;offset++) {
                    var day=LocalState.Day(session.Now.AddDays(offset),r.TimeZone);var weekday=(int)DateTime.ParseExact(day,"yyyy-MM-dd",CultureInfo.InvariantCulture).DayOfWeek;
                    if(r.Mode=="weekdays"&&!r.Weekdays.Contains(weekday))continue;
                    foreach(var t in r.Mode=="weekdays"&&r.WeekSlots.Count>0?r.WeekSlots.Where(s=>s.Weekday==weekday).Select(s=>s.Time):r.Times){var at=LocalState.Parse(LocalState.DueAt(day,t,r.TimeZone));if(at>=session.Now&&at<session.Now.AddDays(7))slots.Add((at,r.Label));}
                }
            }
            foreach(var group in slots.OrderBy(s=>s.At).GroupBy(s=>s.At.ToLocalTime().ToString("ddd, MMM d",CultureInfo.GetCultureInfo("en-US")))){ImGui.TextColored(Style.Accent,group.Key);foreach(var slot in group)ImGui.TextWrapped(slot.At.ToLocalTime().ToString("h:mm tt",CultureInfo.GetCultureInfo("en-US"))+" · "+slot.Label);ImGui.Separator();}
            if(slots.Count==0)ImGui.TextColored(Style.Muted,"No scheduled routines in the next seven days.");
        }
        foreach(var routine in state.Routines) {
            Style.BeginCard(routine.Id,routine.Label,routine.Enabled?"Enabled":"Disabled");
            ImGui.TextWrapped(routine.Mode=="once"?PortalPresentation.Date(routine.OneOffUtc):string.Join(" · ",routine.Times.Select(t=>LocalClock.ScheduleTime(t,routine.TimeZone,null,session.Now)))+" · Your local time");
            if(ImGui.Button("Edit routine")){routineId=routine.Id;routineLabel=routine.Label;mode=routine.Mode;times=string.Join(',',routine.Times.Select(t=>LocalClock.ScheduleTime(t,routine.TimeZone,null,session.Now)));weeklyTimes=string.Join(", ",V6Local.LocalSlots(routine,session.Now).Select(s=>s.Weekday+" "+s.Time));once=LocalClock.Input(routine.OneOffUtc);missed=routine.MissedPolicy;catchUp=routine.CatchUpDays;routineEnabled=routine.Enabled;for(var i=0;i<7;i++)weekdays[i]=routine.Weekdays.Contains(i);editor.Load(routine.Label,routine.Label,routine.Responses,routine.Policy);}
            ImGui.SameLine();if(ImGui.Button("Disable future occurrences"))Send(session,"routineCancel",new{id=routine.Id,cancelPending=false});
            if(ImGui.Button("Cancel routine and pending occurrences"))Send(session,"routineCancel",new{id=routine.Id,cancelPending=true});Style.EndCard();
        }
        if(ImGui.Button("New routine")){routineId=routineLabel=once="";mode="daily";times="18:00";routineEnabled=true;editor.Load("","",new(),new());}
        Style.BeginCard("routine-editor",routineId.Length>0?"Edit routine":"New routine","All entry times use your local clock");
        Input("Routine label",ref routineLabel,160);
        if(Style.BeginCombo("Schedule",mode)){foreach(var choice in new[]{"daily","weekdays","once"})if(ImGui.Selectable(PortalPresentation.State(choice),mode==choice))mode=choice;ImGui.EndCombo();}
        if(mode=="once")Input("Date and time (yyyy-MM-dd HH:mm)",ref once,24);else if(mode=="weekdays")Input("Weekday slots (0 = Sun … 6 = Sat; e.g. 1 18:00, 3 21:00)",ref weeklyTimes,1000);else Input("Daily times (HH:mm, comma separated)",ref times,72);

        ImGui.Checkbox("Enabled",ref routineEnabled);
        if(Style.BeginCombo("Missed-day handling",missed)){foreach(var choice in new[]{"today","individual","group"})if(ImGui.Selectable(choice,choice==missed))missed=choice;ImGui.EndCombo();}
        Style.InputInt("Catch-up window in days (1–31)",ref catchUp);
        editor.Text=routineLabel;editor.Title=routineLabel;editor.Draw(session.Dashboard!.Identity.Name,true);
        var weeklySlots=new List<WeeklySlot>();var validSlots=true;if(mode=="weekdays")foreach(var token in weeklyTimes.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)){var parts=token.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(parts.Length!=2||!int.TryParse(parts[0],out var day)||day is <0 or >6||!MasterPortalPolicy.ValidTime(parts[1])){validSlots=false;break;}weeklySlots.Add(new(){Weekday=day,Time=parts[1]});}
        var scheduleTimes=times.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToList();if(mode=="weekdays")scheduleTimes=weeklySlots.Select(s=>s.Time).Distinct().ToList();var validDate=LocalClock.TryInput(once,out var oneOff);
        ImGui.BeginDisabled(!editor.Valid||string.IsNullOrWhiteSpace(routineLabel)||catchUp is <1 or >31||mode=="once"&&(!validDate||oneOff is null)||mode=="weekdays"&&(!validSlots||weeklySlots.Count==0||weeklySlots.Count>84)||mode!="once"&&(scheduleTimes.Count==0||scheduleTimes.Count>(mode=="weekdays"?84:12)||scheduleTimes.Any(t=>!MasterPortalPolicy.ValidTime(t))));
        if(Style.PrimaryButton("Save routine",new Vector2(-1,40)))Send(session,"routine",new{id=routineId.Length>0?routineId:null,label=routineLabel,enabled=routineEnabled,mode,times=scheduleTimes,weekdays=mode=="weekdays"?weeklySlots.Select(s=>s.Weekday).Distinct().ToList():Enumerable.Range(0,7).ToList(),weekSlots=weeklySlots,oneOffUtc=oneOff,timeZone=LocalClock.ZoneId,missedPolicy=missed,catchUpDays=catchUp,responses=editor.Responses,policy=editor.Policy});
        ImGui.EndDisabled();Style.EndCard();
    }
    private void Prompts(MasterPortalSession session,AdminPet pet)
    {
        var state=pet.Features.V6;Style.Title("Scheduled messages","Delivery modes, priority, responses, and optional expiration");
        if(Style.BeginCombo("Saved interaction template",state.Templates.FirstOrDefault(t=>t.Id==templateId)?.Name??"Choose a template")){foreach(var template in state.Templates){ImGui.PushID(template.Id);if(ImGui.Selectable(template.Name)){templateId=template.Id;editor.TemplateName=template.Name;editor.Load(template.Title,template.Text,template.Responses,template.Policy);}ImGui.PopID();}ImGui.EndCombo();}
        Style.BeginCard("message-editor",promptId.Length>0?"Revise pending message":"Compose message","");editor.Draw(session.Dashboard!.Identity.Name);editor.Dates();
        LocalClock.TryInput(editor.Send,out var send);LocalClock.TryInput(editor.Expires,out var expires);
        ImGui.BeginDisabled(!editor.Valid||expires is not null&&LocalState.Parse(expires)<=LocalState.Parse(send??LocalState.Stamp(session.Now))||FeaturePortalView.Paused(pet));
        if(Style.PrimaryButton(promptId.Length>0?"Save revision":"Queue message",new Vector2(-1,40)))Send(session,promptId.Length>0?"promptRevise":"advancedPrompt",new{id=promptId.Length>0?promptId:null,title=editor.Title,text=editor.Text,sendAtUtc=send,expiresAtUtc=expires,responses=editor.Responses,policy=editor.Policy});ImGui.EndDisabled();
        if(ImGui.Button("New message")){promptId=templateId="";editor.Send=editor.Expires="";editor.Load("","",new(),new());}
        if(templateId.Length>0&&ImGui.Button("Remove selected template")){Send(session,"interactionTemplate",new{id=templateId,remove=true});templateId="";}
        if(ImGui.CollapsingHeader("Save interaction template")){Input("Template name",ref editor.TemplateName,80);ImGui.BeginDisabled(!editor.Valid||string.IsNullOrWhiteSpace(editor.TemplateName));if(ImGui.Button("Save template"))Send(session,"interactionTemplate",new{id=templateId.Length>0?templateId:null,name=editor.TemplateName,title=editor.Title,text=editor.Text,responses=editor.Responses,policy=editor.Policy});ImGui.EndDisabled();}
        Style.EndCard();
        foreach(var prompt in state.Prompts.GroupBy(p=>p.RoutineId??p.Id)) {
            ImGui.PushID(prompt.Key);var first=prompt.First();
            if(ImGui.CollapsingHeader(first.Title+" · "+prompt.Count()+" occurrence(s)",first.MissedPolicy=="group"?ImGuiTreeNodeFlags.None:ImGuiTreeNodeFlags.DefaultOpen))foreach(var p in prompt) {
                Style.BeginCard(p.Id,p.Title,PortalPresentation.State(p.State)+" · "+PortalPresentation.Date(p.SendAtUtc));ImGui.TextWrapped(p.Text);
                if(p.State=="pending") {
                    if(ImGui.Button("Revise")){promptId=p.Id;editor.Load(p.Title,p.Text,p.Responses,p.Policy);editor.Send=LocalClock.Input(p.SendAtUtc);editor.Expires=LocalClock.Input(p.ExpiresAtUtc);}
                    ImGui.SameLine();if(ImGui.Button("Cancel occurrence"))Send(session,"promptCancel",new{id=p.Id});
                }
                if(ImGui.CollapsingHeader("Change and response history"))foreach(var entry in p.History)ImGui.TextWrapped(PortalPresentation.Date(entry.AtUtc)+" · "+PortalPresentation.State(entry.State)+" · "+entry.Text);Style.EndCard();
            }ImGui.PopID();
        }
    }
    private void Gags(MasterPortalSession session,AdminPet pet)
    {
        var state=pet.Features.V6;Style.Title("Gag profiles","Saved voices, exclusions, and timed activation");ImGui.TextWrapped("Current: "+(state.Gag.Configured?state.Gag.Enabled?state.Gag.Name+" · Enabled":"Off":pet.Settings.GarbleEnabled?"Basic gag enabled":"Off"));
        if(Style.BeginCombo("Profile",state.GagProfiles.FirstOrDefault(p=>p.Id==profileId)?.Name??"Choose a profile")){foreach(var profile in state.GagProfiles){ImGui.PushID(profile.Id);if(ImGui.Selectable(profile.Name)){profileId=profile.Id;profileName=profile.Name;profileStyle=profile.Style;strength=profile.Strength;syllables=profile.Syllables;excluded.Clear();excluded.UnionWith(profile.ExcludedChannels);}ImGui.PopID();}ImGui.EndCombo();}
        Style.InputInt("Activation minutes (0 = until disabled)",ref gagMinutes);ImGui.BeginDisabled(profileId.Length==0||gagMinutes is <0 or >10080||FeaturePortalView.Paused(pet));if(Style.PrimaryButton("Enable selected gag",new Vector2(-1,40)))Send(session,"gagApply",new{profileId,enabled=true,minutes=gagMinutes});ImGui.EndDisabled();if(ImGui.Button("Disable gag"))Send(session,"gagApply",new{enabled=false,minutes=0});
        Style.BeginCard("gag-editor","Profile editor","");Input("Profile name",ref profileName,80);
        if(Style.BeginCombo("Voice style",profileStyle)){foreach(var style in new[]{"muffled","soft","playful","custom"})if(ImGui.Selectable(style,style==profileStyle))profileStyle=style;ImGui.EndCombo();}
        ImGui.SliderInt("Strength",ref strength,1,100);if(profileStyle=="custom")Input("Custom syllable letters",ref syllables,24);
        if(ImGui.CollapsingHeader("Excluded chat channels"))foreach(var channel in new[]{"default","say","tell","party","alliance","freecompany","linkshell","cwlinkshell","yell","shout","novice","pvpteam","echo"}){var on=excluded.Contains(channel);if(ImGui.Checkbox(channel,ref on)){if(on)excluded.Add(channel);else excluded.Remove(channel);}}
        Input("Local speech preview",ref gagPreview,200);ImGui.TextWrapped(SpeechGarbler.Body(gagPreview,strength,profileStyle,syllables));
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(profileName)||profileStyle=="custom"&&(syllables.Length==0||syllables.Any(c=>!char.IsLetter(c))));
        if(Style.PrimaryButton("Save gag profile",new Vector2(-1,40)))Send(session,"gagProfile",new{id=profileId.Length>0?profileId:null,name=profileName,style=profileStyle,strength,syllables,excludedChannels=excluded.ToList()});ImGui.EndDisabled();
        if(profileId.Length>0&&ImGui.Button("Remove selected gag profile")){Send(session,"gagProfile",new{id=profileId,remove=true});profileId="";}
        if(ImGui.Button("New profile")){profileId=profileName=syllables="";profileStyle="muffled";strength=100;excluded.Clear();}Style.EndCard();
    }
    private void Points(MasterPortalSession session,AdminPet pet)
    {
        var state=pet.Features.V6;Style.Title("Rewards & Punishments","Separate balances, reasons, rewards, and approval");
        if(ImGui.BeginTable("point-balances",2,ImGuiTableFlags.SizingStretchSame)){ImGui.TableNextColumn();Style.Metric("Reward Points",state.Points.Reward.ToString("N0"),"reward");ImGui.TableNextColumn();Style.Metric("Punishment Points",state.Points.Punishment.ToString("N0"),"punishment");ImGui.EndTable();}
        Style.BeginCard("point-adjustment","Adjust points","Positive adds; negative subtracts. Every change keeps its reason.");
        if(Style.BeginCombo("Balance",pointKind)){foreach(var kind in new[]{"reward","punishment"})if(ImGui.Selectable(kind,kind==pointKind))pointKind=kind;ImGui.EndCombo();}Style.InputInt("Point change",ref delta);Input("Reason",ref pointNote,200);
        ImGui.BeginDisabled(delta is <-1000000 or >1000000);if(Style.PrimaryButton("Record adjustment",new Vector2(-1,40)))Send(session,"points",new{kind=pointKind,delta,note=pointNote});ImGui.EndDisabled();Style.EndCard();
        if(ImGui.CollapsingHeader("Reward catalog",ImGuiTreeNodeFlags.DefaultOpen)) {
            foreach(var reward in state.Rewards){ImGui.PushID(reward.Id);ImGui.TextWrapped(reward.Name+" · "+reward.Cost+" points · "+(reward.Enabled?"Enabled":"Disabled"));if(ImGui.Button("Edit reward")){rewardId=reward.Id;rewardName=reward.Name;rewardDescription=reward.Description;rewardCost=reward.Cost;rewardEnabled=reward.Enabled;}ImGui.PopID();}
            Input("Reward name",ref rewardName,100);Input("Description",ref rewardDescription,1000);Style.InputInt("Cost",ref rewardCost);ImGui.Checkbox("Available to request",ref rewardEnabled);
            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(rewardName)||rewardCost is <1 or >1000000);if(ImGui.Button("Save reward"))Send(session,"rewardDefinition",new{id=rewardId.Length>0?rewardId:null,name=rewardName,description=rewardDescription,cost=rewardCost,enabled=rewardEnabled});ImGui.EndDisabled();ImGui.SameLine();if(ImGui.Button("New reward")){rewardId=rewardName=rewardDescription="";rewardCost=10;rewardEnabled=true;}
        }
        Style.BeginCard("redemptions","Reward requests","");Input("Review note",ref reviewNote,200);
        foreach(var request in state.Redemptions){ImGui.PushID(request.Id);ImGui.TextWrapped(request.Name+" · "+request.Cost+" points · "+PortalPresentation.State(request.State));ImGui.TextWrapped(PortalPresentation.Date(request.RequestedAtUtc));if(request.State=="pending"){ImGui.BeginDisabled(state.Points.Reward<request.Cost);if(ImGui.Button("Approve redemption"))Send(session,"redemptionReview",new{id=request.Id,approve=true,note=reviewNote});ImGui.EndDisabled();ImGui.SameLine();if(ImGui.Button("Decline redemption"))Send(session,"redemptionReview",new{id=request.Id,approve=false,note=reviewNote});}ImGui.PopID();}Style.EndCard();
        if(ImGui.CollapsingHeader("Task-linked points")) {
            if(Style.BeginCombo("Task",pet.Features.Tasks.FirstOrDefault(t=>t.Id==taskId)?.Title??"Choose a task")){foreach(var task in pet.Features.Tasks){ImGui.PushID(task.Id);if(ImGui.Selectable(task.Title)){taskId=task.Id;var rule=state.TaskPointRules.FirstOrDefault(r=>r.TaskId==task.Id);taskReward=rule?.Reward??0;taskReduction=rule?.PunishmentReduction??0;}ImGui.PopID();}ImGui.EndCombo();}
            Style.InputInt("Reward points on Master approval",ref taskReward);Style.InputInt("Punishment-point reduction on approval",ref taskReduction);
            ImGui.BeginDisabled(taskId.Length==0||taskReward is <0 or >1000000||taskReduction is <0 or >1000000);if(ImGui.Button("Save task points"))Send(session,"taskPoints",new{taskId,reward=taskReward,punishmentReduction=taskReduction});ImGui.EndDisabled();
        }
        Style.Title("Point history","Corrections are recorded as new adjustments");
        foreach(var entry in state.Points.Entries.Concat(olderPoints).DistinctBy(e=>e.Id)){ImGui.TextWrapped((entry.Delta>=0?"+":"")+entry.Delta+" "+entry.Kind+" · "+PortalPresentation.Date(entry.AtUtc));ImGui.TextWrapped(entry.Note);ImGui.Separator();}
        var cursor=olderPoints.Count==0?state.Points.NextCursor:pointCursor;if(cursor is not null&&ImGui.Button("Load older point history"))Send(session,"pointsHistory",new{before=cursor});
    }
}
