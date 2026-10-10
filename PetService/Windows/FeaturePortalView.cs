using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures;
using PetService.Core;
namespace PetService.Windows;
internal sealed class FeaturePortalView:IDisposable
{
 private string petName="",name="",theme="#baa0ff",query="",bag="",inventoryCharacter="",taskId="",taskTitle="",taskDescription="",due="",checklist="",ledgerNote="",reminderId="",label="",time="18:00";
 private string identityBaseline="",previewDetail="";private readonly Dictionary<string,string> reviewNotes=[];
 private bool useName,review,reply,resolves=true,enabled=true,taskEditor,reminderEditor;private int lockMinutes,rewardPoints,priorityIndex=1,repeatIndex;private string focusedTask="";private readonly List<ResponseOption> options=[];
 private readonly IntegrationPortalView integrations=new();
 private readonly FileDialogManager dialog=new();private readonly PortraitView portrait=new();private string? portraitData;private bool removePortrait;private string error="";
 internal bool EditingText{get;private set;}
 internal string InventoryCharacter=>inventoryCharacter;internal string IntegrationCharacter=>integrations.Character;
 private void Input(string label,ref string value,int max=1000){ImGui.TextUnformatted(label);ImGui.SetNextItemWidth(-12);ImGui.InputText("##"+label,ref value,max);EditingText|=ImGui.IsItemActive();}
 private void Action(MasterPortalSession s,string action,object data,bool global=false)=>s.Action(new(){Action=action,Pet=global?null:s.Selected,Data=data});
 internal void Begin(MasterPortalSession s){EditingText=false;dialog.Draw();if(s.Selected==petName)return;petName=s.Selected;ResetPetDrafts();}
 private void ResetPetDrafts(){reminderId=taskId=label=taskTitle=taskDescription=due=checklist=ledgerNote=query=inventoryCharacter=bag=previewDetail=focusedTask="";integrations.Clear();taskEditor=reminderEditor=false;options.Clear();reviewNotes.Clear();time="18:00";reply=review=false;resolves=enabled=true;lockMinutes=rewardPoints=repeatIndex=0;priorityIndex=1;}
 internal void Completed(AdminAction action){if(action.Action=="task")taskEditor=false;if(action.Action=="reminder")reminderEditor=false;if(action.Action=="identity"){portraitData=null;removePortrait=false;identityBaseline="";portrait.ResetPreview();}}
 internal void Identity(MasterPortalSession s)
 {
  Style.BeginCard("master-display","Master identity","One setting controls your display name across all linked pets and enabled characters");
  var current=s.Dashboard!.Identity;var draft=JsonSerializer.Serialize(new{name,useName,theme});
  if(identityBaseline.Length==0||draft==identityBaseline){name=current.DisplayName;useName=current.UseName;theme=current.Theme;identityBaseline=JsonSerializer.Serialize(new{name,useName,theme});}
  Input("Display name",ref name,80);ImGui.Checkbox("Display my name instead of Master",ref useName);
  Style.ColorField("Theme color",ref theme);EditingText|=ImGui.IsAnyItemActive();
  ImGui.TextWrapped("Pet-facing preview: Message from "+(useName?name:"Master"));Style.EndCard();
  Style.BeginCard("master-portrait","Portrait","Choose a PNG up to 2 MB and 4096 × 4096 pixels");
  if(!removePortrait)portrait.Set(current.PortraitRevision,s.Portrait);portrait.Draw();
  if(ImGui.Button("Choose portrait PNG…"))dialog.OpenFileDialog("Master portrait",".png",(ok,path)=>{
   if(!ok)return;try{var info=new FileInfo(path);if(info.Length>2000000)throw new InvalidOperationException();var bytes=File.ReadAllBytes(path);if(!PngIcon.PortraitHeaderValid(bytes))throw new InvalidOperationException();portraitData=Convert.ToBase64String(bytes);removePortrait=false;portrait.Preview(bytes);error="";}catch{error="Choose a valid PNG no larger than 2 MB and 4096 × 4096 pixels.";}
  });
  if(ImGui.Button("Remove portrait")){removePortrait=true;portraitData=null;portrait.Dispose();}if(error.Length>0)ImGui.TextWrapped(error);Style.EndCard();
  ImGui.BeginDisabled(useName&&string.IsNullOrWhiteSpace(name));
  if(Style.PrimaryButton("Save Master identity",new Vector2(-1,40)))Action(s,"identity",new{displayName=name.Trim(),useName,theme,portrait=portraitData,removePortrait},true);
  ImGui.EndDisabled();
 }
 internal void Controls(MasterPortalSession s,AdminPet p){Style.BeginCard("bonding-control","Bonding","Keep the game view visible and native chat usable");var l=p.Features.Lock;ImGui.TextWrapped("State: "+(l?.State??"released")+(l?.Detail.Length>0?" · "+l.Detail:""));Style.Metric("Time remaining",ControlPresentation.Remaining(l,s.Now),"lockremaining");Style.InputInt("Duration minutes (0 = until released)",ref lockMinutes);ImGui.BeginDisabled(Paused(p)||lockMinutes is <0 or >10080);if(ImGui.Button("Bond pet",new Vector2(210,40)))Action(s,"lock",new{enabled=true,minutes=lockMinutes});ImGui.EndDisabled();ImGui.SameLine();if(ImGui.Button("Release bond",new Vector2(210,40)))Action(s,"lock",new{enabled=false,minutes=0});ImGui.TextWrapped("The pet's kill switch and safe release remain available. Game activity continues while input is blocked.");
  Style.EndCard();Style.BeginCard("sleep-control","Sleep","Play Dead, with gameplay and outgoing chat blocked");var sleep=p.Features.Sleep;ImGui.TextWrapped("State: "+(sleep?.State??"released")+(sleep?.Detail.Length>0?" · "+sleep.Detail:""));ImGui.BeginDisabled(Paused(p));if(ImGui.Button("Sleep",new Vector2(210,40)))Action(s,"sleep",new{enabled=true});ImGui.EndDisabled();ImGui.SameLine();if(ImGui.Button("Wake",new Vector2(210,40)))Action(s,"sleep",new{enabled=false});ImGui.TextWrapped("Emergency Wake Up ends only that Sleep command. Master control remains active.");
  Style.EndCard();if(ImGui.CollapsingHeader("Control history")){var records=p.Features.ControlHistory.Concat(s.OlderControlHistory).DistinctBy(x=>x.Id);foreach(var e in records){ImGui.TextWrapped(Local(e.AtUtc)+" · "+PortalPresentation.State(e.Kind)+" / "+PortalPresentation.State(e.Operation)+" · "+PortalPresentation.State(e.State));if(e.Detail.Length>0)ImGui.TextWrapped(e.Detail);if(e.State=="requested")ImGui.TextUnformatted(e.DurationMinutes>0?$"Duration: {e.DurationMinutes} minutes":e.Kind=="lock"&&e.Operation=="enable"?"Until released":"");}var cursor=s.OlderControlHistory.Count==0?p.Features.ControlHistoryCursor:s.ControlCursor;ImGui.BeginDisabled(s.Busy||string.IsNullOrEmpty(cursor));if(ImGui.Button("Load older control history"))s.LoadOlderControls();ImGui.EndDisabled();}}

 internal static bool Paused(AdminPet p)=>p.Features.Presence.State is "unapproved" or "paused" or "waiting";
 internal void Reminders(MasterPortalSession s,AdminPet p)
 {
  Style.Title("Daily reminders","Choose all response buttons and enter daily times on your local clock");
  if(Style.PrimaryButton("New reminder",new Vector2(220,40))){previewDetail="";reminderId="";label="";time="18:00";options.Clear();reply=false;resolves=enabled=true;reminderEditor=true;}
  foreach(var reminder in p.Reminders) {
   Style.BeginCard(reminder.Id,reminder.Label,LocalClock.ScheduleTime(reminder.Time,reminder.TimeZone,reminder.NextDueAtUtc,s.Now)+" · Your local time · "+(reminder.Enabled?"Enabled":"Disabled"));
   if(reminder.Responses is null)ImGui.TextColored(Style.Accent,"Response setup required");
   if(ImGui.Button("Edit reminder")) {
    previewDetail="";reminderId=reminder.Id;label=reminder.Label;time=LocalClock.ScheduleTime(reminder.Time,reminder.TimeZone,reminder.NextDueAtUtc,s.Now);enabled=reminder.Enabled;options.Clear();resolves=true;
    if(reminder.Responses is { } savedResponses){options.AddRange(JsonSerializer.Deserialize<List<ResponseOption>>(JsonSerializer.Serialize(savedResponses.Options,ServiceClient.Json),ServiceClient.Json)!);reply=savedResponses.AllowReply;resolves=savedResponses.ReplyResolves;}else reply=false;reminderEditor=true;
   }
   ImGui.SameLine();ImGui.BeginDisabled(!reminder.Enabled);if(ImGui.Button("Disable reminder"))s.Action(new(){Action="disableReminder",Pet=p.Name,ReminderId=reminder.Id});ImGui.EndDisabled();Style.EndCard();
  }
  if(p.Reminders.Count==0)ImGui.TextColored(Style.Muted,"No reminders yet.");
  if(!reminderEditor)return;
  Style.BeginCard("reminder-editor",reminderId.Length>0?"Edit daily reminder":"New daily reminder","Response buttons are chosen by you");
  Input("Label",ref label,160);Input("Your daily time (HH:mm)",ref time,5);ImGui.Checkbox("Enabled",ref enabled);
  for(var i=0;i<options.Count;i++){var o=options[i];ImGui.PushID(o.Id);var ol=o.Label;Input("Button label",ref ol,80);o.Label=ol;var text=o.Text;Input("Returned text",ref text,1000);o.Text=text;if(Style.BeginCombo("Action",o.Action)){foreach(var action in new[]{"complete","decline","response","snooze","random-snooze"})if(ImGui.Selectable(action,o.Action==action)){o.Action=action;if(action!="snooze")o.Minutes=null;o.Resolves=action is "complete" or "decline";}ImGui.EndCombo();}if(o.Action=="snooze"){var presetMinutes=o.Minutes??0;if(Style.InputInt("Fixed minutes (0 = pet chooses)",ref presetMinutes))o.Minutes=Math.Clamp(presetMinutes,0,10);}
   if(o.Action=="response"){var done=o.Resolves;if(ImGui.Checkbox("Resolve reminder",ref done))o.Resolves=done;}if(ImGui.SmallButton("Remove")){options.RemoveAt(i);ImGui.PopID();break;}ImGui.SameLine();if(i>0&&ImGui.SmallButton("Move up")){(options[i-1],options[i])=(options[i],options[i-1]);ImGui.PopID();break;}ImGui.Separator();ImGui.PopID();}
  if(options.Count<32&&ImGui.Button("Add response button"))options.Add(new());ImGui.Checkbox("Allow a written reply",ref reply);if(reply)ImGui.Checkbox("Written reply resolves reminder",ref resolves);
  var config=new ResponseOptions{Options=options,AllowReply=reply,ReplyResolves=resolves};
  if(ImGui.CollapsingHeader("Preview daily reminder")){ImGui.TextColored(Style.Accent,"Daily reminder");ImGui.TextWrapped(label.Length>0?label:"Your reminder text");ImGui.TextUnformatted(time+" · your local time");ImGui.TextWrapped("Preview only. Click a button to see its behavior; nothing is sent.");foreach(var o in options){if(Style.LiteralButton(o.Label.Length>0?o.Label:"(Empty label)","preview"+o.Id,new Vector2(-1,32)))previewDetail=ControlPresentation.Response(o)+(o.Text.Length>0?" Returned text: "+o.Text:"");ImGui.TextWrapped(ControlPresentation.Response(o));}if(reply){ImGui.TextUnformatted("Written reply field");if(ImGui.Button("Preview written reply"))previewDetail=resolves?"Reports the written reply and closes this occurrence.":"Reports the written reply and leaves this occurrence open.";}ImGui.TextWrapped(previewDetail);if(!config.Usable)ImGui.TextColored(Style.Accent,"Add a response or written reply before enabling.");}
ImGui.TextWrapped("Chosen and random snooze use 1–10 minutes. Random time is shown only to you.");ImGui.BeginDisabled(enabled&&!config.Usable||options.Any(o=>string.IsNullOrWhiteSpace(o.Label))||string.IsNullOrWhiteSpace(label)||!MasterPortalPolicy.ValidTime(time));if(ImGui.Button("Save daily reminder",new Vector2(-1,40)))Action(s,"reminder",new{id=reminderId.Length>0?reminderId:null,label,time,timeZone=LocalClock.ZoneId,enabled,responses=config});ImGui.EndDisabled();if(ImGui.Button("Close reminder editor"))reminderEditor=false;Style.EndCard();
 }
 internal void Inventory(MasterPortalSession s,AdminPet p){Style.Title("Inventory and gil","Last observed contents by character");if(p.Features.SnapshotIssues.TryGetValue("inventory",out var inventoryIssue))ImGui.TextWrapped("Latest inventory upload was rejected ("+inventoryIssue+"). Any contents below are from the last accepted report.");ImGui.BeginDisabled(Paused(p));if(ImGui.Button("Request fresh inventory",new Vector2(230,42)))Action(s,"inventory",new{});ImGui.EndDisabled();if(p.Features.Inventory.Count==0){ImGui.TextWrapped("No inventory snapshot yet. Open the relevant containers on the pet's allowed character.");return;}
  if(Style.BeginCombo("Inventory character",inventoryCharacter)){foreach(var x in p.Features.Inventory)if(ImGui.Selectable(x.CharacterName+" @ "+x.HomeWorld,x.CharacterId==inventoryCharacter)){inventoryCharacter=x.CharacterId;bag="";}ImGui.EndCombo();}var snapshot=p.Features.Inventory.FirstOrDefault(x=>x.CharacterId==inventoryCharacter)??p.Features.Inventory[0];inventoryCharacter=snapshot.CharacterId;if(!snapshot.Loaded){ImGui.TextWrapped("Loading this character’s inventory…");return;}Style.Metric("Total gil",snapshot.Gil.ToString("N0"),"gil");ImGui.TextWrapped("Captured "+Local(snapshot.CapturedAtUtc)+(DateTimeOffset.TryParse(snapshot.CapturedAtUtc,out var at)&&(s.Now-at).TotalSeconds>45?" · Last observed":" · Recent"));Input("Find item",ref query,200);
  if(Style.BeginCombo("Container",bag)){foreach(var b in snapshot.Containers)if(ImGui.Selectable(b.Name,b.Id==bag))bag=b.Id;ImGui.EndCombo();}if(snapshot.Containers.Count==0){ImGui.TextWrapped("No loaded containers in this snapshot.");return;}var container=snapshot.Containers.FirstOrDefault(x=>x.Id==bag)??snapshot.Containers[0];bag=container.Id;if(container.CapturedAtUtc is not null)ImGui.TextWrapped("Container observed "+Local(container.CapturedAtUtc));if(!container.Available){ImGui.TextWrapped("This container was not loaded at capture.");return;}var used=container.Slots.Count(x=>x.ItemId!=0);ImGui.TextUnformatted($"{used} occupied · {container.Capacity-used} free · Slot numbers start at 1");if(ImGui.BeginTable("inventorygrid",5,ImGuiTableFlags.SizingStretchSame)){foreach(var item in container.Slots){ImGui.TableNextColumn();ImGui.PushID(item.Slot);var match=query.Length==0||item.Name.Contains(query,StringComparison.OrdinalIgnoreCase);if(!match)ImGui.PushStyleVar(ImGuiStyleVar.Alpha,.25f);var texture=item.IconId==0?null:Plugin.Textures.GetFromGameIcon(new GameIconLookup(item.IconId,item.Hq)).GetWrapOrDefault();if(texture is not null)ImGui.Image(texture.Handle,new Vector2(42));else ImGui.Button("—",new Vector2(42));if(ImGui.IsItemHovered()){ImGui.BeginTooltip();ImGui.TextUnformatted($"Slot {item.Slot}: {(item.ItemId==0?"Empty":item.Name+(item.Hq?" HQ":""))}");ImGui.TextUnformatted("Quantity: "+item.Quantity);ImGui.EndTooltip();}ImGui.TextUnformatted(item.Slot+" · "+(item.ItemId==0?"Empty":item.Quantity.ToString()));if(!match)ImGui.PopStyleVar();ImGui.PopID();}ImGui.EndTable();}
 }
 internal void FocusTask(string id)=>focusedTask=id;
 internal void Tasks(MasterPortalSession s,AdminPet p,Action<string> evidence)
 {
  Style.Title("Tasks","Assignments, submissions, and Master review");
  if(Style.PrimaryButton("New assignment",new Vector2(220,40))){taskId=taskTitle=taskDescription=due=checklist="";priorityIndex=1;repeatIndex=0;review=true;taskEditor=true;}
  if(taskEditor) {
   Style.BeginCard("task-editor",taskId.Length>0?"Edit assignment":"New assignment","Dates are entered and displayed on your local clock");
   Input("Title",ref taskTitle,160);ImGui.TextUnformatted("Description");ImGui.InputTextMultiline("##taskdescription",ref taskDescription,3000,new Vector2(-1,90));EditingText|=ImGui.IsItemActive();
   Input("Due (yyyy-MM-dd HH:mm, blank = none)",ref due,24);var dueValid=LocalClock.TryInput(due,out var dueUtc);if(!dueValid)ImGui.TextWrapped("Enter a local date and time as yyyy-MM-dd HH:mm.");
   ImGui.TextUnformatted("Checklist (one item per line)");ImGui.InputTextMultiline("##taskchecklist",ref checklist,8040,new Vector2(-1,100));EditingText|=ImGui.IsItemActive();
   Style.Combo("Priority",ref priorityIndex,new[]{"Low","Normal","High"},3);Style.Combo("Repeat",ref repeatIndex,new[]{"None","Daily","Weekly"},3);ImGui.Checkbox("Require Master review",ref review);
   var entries=checklist.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToList();
   ImGui.BeginDisabled(string.IsNullOrWhiteSpace(taskTitle)||!dueValid||entries.Count>40||entries.Any(x=>x.Length>200));
   if(Style.PrimaryButton("Save assignment",new Vector2(-1,40)))Action(s,"task",new{id=taskId.Length>0?taskId:null,title=taskTitle.Trim(),description=taskDescription,dueAtUtc=dueUtc,timeZone=LocalClock.ZoneId,priority=new[]{"low","normal","high"}[Math.Clamp(priorityIndex,0,2)],repeat=new[]{"none","daily","weekly"}[Math.Clamp(repeatIndex,0,2)],requireReview=review,checklist=entries});ImGui.EndDisabled();
   if(ImGui.Button("Close editor"))taskEditor=false;Style.EndCard();
  }
  foreach(var task in p.Features.Tasks) {
   Style.BeginCard(task.Id,task.Title,PortalPresentation.State(task.State)+" · "+PortalPresentation.State(task.Priority)+(task.DueAtUtc is not null?" · Due "+Local(task.DueAtUtc):""));
   var focus=focusedTask==task.Id;if(focus){ImGui.SetScrollHereY(.25f);focusedTask="";}
   if(task.Description.Length>0)ImGui.TextWrapped(task.Description);
   foreach(var check in task.Checklist)ImGui.TextWrapped((check.Done?"Done: ":"Remaining: ")+check.Text);
   if(task.Comment.Length>0){ImGui.TextColored(Style.Muted,"Pet submission");ImGui.TextWrapped(task.Comment);}
   if(ImGui.Button("Edit assignment")){taskId=task.Id;taskTitle=task.Title;taskDescription=task.Description;due=LocalClock.Input(task.DueAtUtc);checklist=string.Join('\n',task.Checklist.Select(x=>x.Text));review=task.RequireReview;priorityIndex=Math.Max(0,Array.IndexOf(new[]{"low","normal","high"},task.Priority));repeatIndex=Math.Max(0,Array.IndexOf(new[]{"none","daily","weekly"},task.Repeat));taskEditor=true;}
   ImGui.SameLine();if(ImGui.Button("Evidence ("+task.Evidence.Count+")"))evidence(task.Id);
   if(focus)ImGui.SetNextItemOpen(true,ImGuiCond.Always);
   if(ImGui.CollapsingHeader("Review assignment")) {
    var note=reviewNotes.GetValueOrDefault(task.Id)??"";Input("Review note",ref note);reviewNotes[task.Id]=note;
    ImGui.BeginDisabled(task.State is "completed" or "cancelled");
    if(Style.PrimaryButton("Approve",new Vector2(150,36)))Action(s,"taskReview",new{id=task.Id,state="completed",comment=note});
    if(ImGui.Button("Return for changes"))Action(s,"taskReview",new{id=task.Id,state="pending",comment=note});
    if(ImGui.Button("Cancel task"))Action(s,"taskReview",new{id=task.Id,state="cancelled",comment=note});ImGui.EndDisabled();
   }
   if(ImGui.CollapsingHeader("Task history"))foreach(var entry in task.History)ImGui.TextWrapped(Local(entry.AtUtc)+" · "+PortalPresentation.State(entry.State)+(entry.Comment.Length>0?" · "+entry.Comment:""));
   Style.EndCard();
  }
  if(p.Features.Tasks.Count==0)ImGui.TextColored(Style.Muted,"No assignments yet. Select New assignment to create one.");
 }
 internal void Integrations(MasterPortalSession s,AdminPet p){integrations.Draw(s,p);EditingText|=integrations.EditingText;}
 internal void Rewards(MasterPortalSession s,AdminPet p){var r=p.Features.Rewards;Style.Title("Reward ledger","Optional points recorded by the Master");var on=r.Enabled;if(ImGui.Checkbox("Enable reward ledger",ref on))Action(s,"rewards",new{enabled=on});ImGui.TextUnformatted("Balance: "+r.Balance);Style.InputInt("Points to add or spend",ref rewardPoints);Input("Ledger note",ref ledgerNote,200);if(ImGui.Button("Record points"))Action(s,"rewards",new{points=rewardPoints,note=ledgerNote});foreach(var e in r.Entries.Take(50))ImGui.TextWrapped(Local(e.AtUtc)+" · "+e.Points+" · "+e.Note);}
 private static string Local(string value)=>PortalPresentation.Date(value);
 internal void Clear(){petName="";ResetPetDrafts();dialog.Reset();portrait.Dispose();portraitData=null;removePortrait=false;name="";useName=false;identityBaseline="";}
 public void Dispose()=>Clear();
}
