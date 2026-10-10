using System.Numerics;
using Dalamud.Bindings.ImGui;
using PetService.Core;

namespace PetService.Windows;

internal sealed class TaskEvidenceView : IDisposable
{
    private readonly TaskEvidenceSearch search=new();
    private string selected="",context="",requested="",taskContext="",text="",kind="duty",from="",to="",character="";
    internal bool EditingText{get;private set;}
    internal void Open(string id)=>requested=id;
    private void Input(string label,ref string value,int limit=200){ImGui.TextUnformatted(label);ImGui.SetNextItemWidth(-1);ImGui.InputText("##"+label,ref value,limit);EditingText|=ImGui.IsItemActive();}
    internal void Draw(MasterPortalSession session,AdminPet pet,Action<string> review)
    {
        EditingText=false;
        var nextContext=pet.Name+":"+session.JournalRevision;
        if(context!=nextContext){context=nextContext;selected=taskContext="";search.Dispose();}
        if(requested.Length>0){selected=requested;requested="";}
        var tasks=pet.Features.Tasks;
        if(!tasks.Any(t=>t.Id==selected))selected="";
        var task=tasks.FirstOrDefault(t=>t.Id==selected);
        Style.Title("Task evidence","Choose a task and attach relevant observations for review. Attaching evidence does not approve a task.");
        if(Style.BeginCombo("Task",task is null?"Select a task…":task.Title+" · "+PortalPresentation.State(task.State))) {
            if(ImGui.Selectable("Select a task…",selected.Length==0))selected="";
            foreach(var assignment in tasks){ImGui.PushID(assignment.Id);if(ImGui.Selectable(assignment.Title+" · "+PortalPresentation.State(assignment.State),assignment.Id==selected))selected=assignment.Id;ImGui.PopID();}
            ImGui.EndCombo();
        }
        task=tasks.FirstOrDefault(t=>t.Id==selected);
        var taskKey=nextContext+":"+selected;
        if(taskContext!=taskKey){taskContext=taskKey;search.Select(nextContext,selected);text=to=character="";kind="duty";from=LocalClock.Input(task?.CreatedAtUtc);}
        search.Update();
        if(task is null){ImGui.TextWrapped(tasks.Count==0?"No tasks yet. Create an assignment on the Tasks page.":"Select a task to review its evidence.");return;}
        Style.BeginCard("evidence-task",task.Title,PortalPresentation.State(task.State)+(task.DueAtUtc is not null?" · Due "+PortalPresentation.Date(task.DueAtUtc):""));
        if(task.Description.Length>0)ImGui.TextWrapped(task.Description);
        foreach(var check in task.Checklist)ImGui.TextWrapped((check.Done?"Done: ":"Remaining: ")+check.Text);
        if(ImGui.Button("Open task review"))review(task.Id);
        Style.EndCard();
        Style.BeginCard("evidence-search","Find relevant observations","Matches saved event text. Review each result before attaching it.");
        if(Style.BeginCombo("Event type",ActivityLabels.Kind(kind))) {
            foreach(var type in new[]{"duty","zone","travel","inventory","trade","retainer","job","currency"})if(ImGui.Selectable(ActivityLabels.Kind(type),type==kind))kind=type;
            ImGui.EndCombo();
        }
        Input("Duty, location, item, or other matching text",ref text);
        if(ImGui.BeginTable("evidence-dates",2,ImGuiTableFlags.SizingStretchSame)) {
            ImGui.TableNextColumn();Input("From your clock (yyyy-MM-dd HH:mm)",ref from,24);
            ImGui.TableNextColumn();Input("Before your clock (blank = now)",ref to,24);ImGui.EndTable();
        }
        if(Style.BeginCombo("Character",pet.Features.Characters.FirstOrDefault(c=>c.Id==character)?.Name??"All approved characters")) {
            if(ImGui.Selectable("All approved characters",character.Length==0))character="";
            foreach(var c in pet.Features.Characters.Where(c=>c.Allowed))if(ImGui.Selectable(c.Name+" @ "+c.HomeWorld,c.Id==character))character=c.Id;
            ImGui.EndCombo();
        }
        ImGui.BeginDisabled(search.Busy||string.IsNullOrWhiteSpace(text));
        if(Style.PrimaryButton("Find observations",new Vector2(-1,40)))search.Find(q=>session.ReadJournal(q,false),kind,text,from,to,character);
        ImGui.EndDisabled();Style.EndCard();
        Style.Title("Attached evidence","Saved with this task for your review");
        foreach(var entry in task.Evidence)JournalRecordView.Draw(entry.Record,()=>{
            ImGui.TextColored(Style.Muted,"Attached "+PortalPresentation.Date(entry.AttachedAtUtc));
            if(ImGui.Button("Remove from this task"))session.Action(new(){Action="taskEvidence",Pet=pet.Name,Data=new{taskId=task.Id,recordKey=PortalJson.Text(entry.Record,"key"),remove=true}});
        });
        if(task.Evidence.Count==0)ImGui.TextColored(Style.Muted,"No observations attached to this task.");
        Style.Title("Search results",search.Notice);
        var attached=task.Evidence.Select(e=>PortalJson.Text(e.Record,"key")).ToHashSet(StringComparer.Ordinal);
        var candidates=search.Rows.Where(r=>!attached.Contains(PortalJson.Text(r,"key"))).ToList();
        foreach(var record in candidates)JournalRecordView.Draw(record,()=>{
            if(ImGui.Button("Attach to this task"))session.Action(new(){Action="taskEvidence",Pet=pet.Name,Data=new{taskId=task.Id,recordKey=PortalJson.Text(record,"key")}});
        });
        if(search.Rows.Count>0&&candidates.Count==0)ImGui.TextColored(Style.Muted,"All loaded matches are already attached to this task.");
        ImGui.BeginDisabled(search.Busy||search.Cursor.Length==0);
        if(search.Cursor.Length>0&&ImGui.Button("Load older matches"))search.Find(q=>session.ReadJournal(q,false),kind,text,from,to,character,true);
        ImGui.EndDisabled();
    }
    internal void Hide(){search.Dispose();taskContext="";}
    public void Dispose(){Hide();requested=selected=context="";}
}
