using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using PetService.Core;

namespace PetService.Windows;

internal sealed class IntegrationPortalView
{
    private string target="",context="",title="",color="#ffffff",glow="#ffffff",savedTitle="",statusId="",presetId="",removeId="";
    private bool prefix,glowOn,initialized;
    private int titleMinutes,moodleMinutes,stacks;
    internal string Character=>target;
    internal bool EditingText{get;private set;}
    internal void Clear(){target=context="";Reset();}
    private void Reset(){title=savedTitle=statusId=presetId=removeId="";color=glow="#ffffff";prefix=glowOn=initialized=false;titleMinutes=moodleMinutes=stacks=0;}
    private void LoadTitle(JsonElement value)
    {
        title=PortalJson.Text(value,"title");prefix=PortalJson.Bool(value,"prefix");
        color=Hex(PortalJson.Text(value,"colour"));var savedGlow=PortalJson.Text(value,"glow");
        glowOn=ValidHex(savedGlow);glow=Hex(savedGlow);initialized=true;
    }
    private static bool ValidHex(string value)=>value.Length==7&&value[0]=='#'&&uint.TryParse(value.AsSpan(1),System.Globalization.NumberStyles.HexNumber,null,out _);
    private static string Hex(string value)=>ValidHex(value)?value:"#ffffff";
    private void Send(MasterPortalSession session,string kind,object data)=>session.Action(new(){Action=kind,Pet=session.Selected,Data=data});
    internal void Draw(MasterPortalSession session,AdminPet pet)
    {
        EditingText=false;
        Style.Title("Honorific and Moodles","Saved titles, statuses, and presets from the pet’s compatible plugins");
        var allowed=pet.Features.Characters.Where(c=>c.Allowed).ToList();
        if(!allowed.Any(c=>c.Id==target))target=allowed.FirstOrDefault()?.Id??"";
        if(Style.BeginCombo("Approved character",allowed.FirstOrDefault(c=>c.Id==target) is { } selected?selected.Name+" @ "+selected.HomeWorld:"No approved characters")) {
            foreach(var character in allowed)if(ImGui.Selectable(character.Name+" @ "+character.HomeWorld,character.Id==target))target=character.Id;
            ImGui.EndCombo();
        }
        if(context!=pet.Name+":"+target){context=pet.Name+":"+target;Reset();}
        var catalog=IntegrationCatalog.Find(pet.Features,target);
        var honorific=PortalJson.Get(catalog,"honorific");var moodles=PortalJson.Get(catalog,"moodles");
        var hAvailable=PortalJson.Bool(honorific,"available");var mAvailable=PortalJson.Bool(moodles,"available");
        var blocked=pet.Archived||FeaturePortalView.Paused(pet)||target.Length==0;
        if(catalog.ValueKind!=JsonValueKind.Object)ImGui.TextWrapped("No integration catalog for this character. Log in on it and request a fresh report.");
        else ImGui.TextWrapped("Availability and statuses come from the last accepted report. Requests wait for the selected character to connect.");
        if(pet.Features.SnapshotIssues.ContainsKey("catalog"))ImGui.TextWrapped("The latest catalog could not be accepted. Request a fresh report; the last accepted catalog is shown.");
        ImGui.BeginDisabled(blocked||!pet.Paired);
        if(Style.PrimaryButton("Request fresh integration report",new Vector2(-1,40)))session.Action(new(){Action="requestReport",Pet=pet.Name});
        ImGui.EndDisabled();
        var titles=PortalJson.Array(honorific,"titles");
        if(savedTitle.Length>0&&!titles.Any(t=>IntegrationCatalog.TitleKey(t)==savedTitle))savedTitle="";
        if(hAvailable&&!initialized)LoadTitle(PortalJson.Get(honorific,"current"));
        var statuses=PortalJson.Array(moodles,"statuses");var presets=PortalJson.Array(moodles,"presets");
        if(!statuses.Any(s=>PortalJson.Text(s,"id")==statusId))statusId="";
        if(!presets.Any(p=>PortalJson.Text(p,"id")==presetId))presetId="";
        var managed=IntegrationCatalog.Managed(pet.Features,catalog);
        var current=PortalJson.Array(moodles,"current");
        var removable=current.Where(s=>managed.Contains(PortalJson.Text(s,"id"))&&IntegrationCatalog.SafeStatus(s)).ToList();
        if(!removable.Any(s=>PortalJson.Text(s,"id")==removeId))removeId="";
        var columns=ImGui.GetContentRegionAvail().X>=780?2:1;
        if(ImGui.BeginTable("integrations",columns,ImGuiTableFlags.SizingStretchSame)) {
            ImGui.TableNextColumn();
            DrawHonorific(session,blocked,hAvailable,honorific,titles);
            ImGui.TableNextColumn();
            DrawMoodles(session,blocked,mAvailable,moodles,statuses,presets,current,removable,managed);
            ImGui.EndTable();
        }
        Style.BeginCard("integration-results","Recent requests","Queued requests are confirmed after the pet’s plugin applies them");
        var commands=pet.Features.IntegrationCommands.Where(c=>c.CharacterId==target).TakeLast(12).Reverse().ToList();
        foreach(var command in commands) {
            ImGui.PushID(command.Id);ImGui.TextColored(command.State=="applied"?Style.Good:Style.Accent,(command.Kind=="honorific"?"Honorific":"Moodles")+" · "+PortalPresentation.State(command.Operation)+" · "+PortalPresentation.State(command.State));
            ImGui.TextWrapped(PortalPresentation.Date(command.RequestedAtUtc));
            if(command.Detail.Length>0)ImGui.TextWrapped(command.Detail);ImGui.Separator();ImGui.PopID();
        }
        if(commands.Count==0)ImGui.TextColored(Style.Muted,"No integration requests for this character.");
        Style.EndCard();EditingText|=ImGui.IsAnyItemActive();
    }
    private void DrawHonorific(MasterPortalSession session,bool blocked,bool available,JsonElement honorific,List<JsonElement> titles)
    {
        Style.BeginCard("honorific","Honorific","Choose a saved title or enter your own text");
        Availability(available,PortalJson.Text(honorific,"version"));
        var current=PortalJson.Get(honorific,"current");
        ImGui.TextWrapped(PortalJson.Text(current,"title") is {Length:>0} text?"Last reported title: "+text+(PortalJson.Bool(current,"prefix")?" · Before name":" · After name"):"No current title was reported.");
        var selection=titles.FirstOrDefault(t=>IntegrationCatalog.TitleKey(t)==savedTitle);
        if(Style.BeginCombo("Saved title",savedTitle.Length>0?IntegrationCatalog.TitleLabel(selection):"Custom / current title")) {
            if(ImGui.Selectable("Custom / current title",savedTitle.Length==0))savedTitle="";
            foreach(var t in titles) {
                var key=IntegrationCatalog.TitleKey(t);ImGui.PushID(key);
                if(ImGui.Selectable(IntegrationCatalog.TitleLabel(t),key==savedTitle)){savedTitle=key;LoadTitle(t);}
                ImGui.PopID();
            }
            ImGui.EndCombo();
        }
        ImGui.TextUnformatted("Title text");
        if(ImGui.InputText("##title",ref title,200)){initialized=true;savedTitle="";}EditingText|=ImGui.IsItemActive();
        if(Style.ColorField("Title color",ref color)){initialized=true;savedTitle="";}
        if(ImGui.Checkbox("Enable glow",ref glowOn)){initialized=true;savedTitle="";}
        ImGui.BeginDisabled(!glowOn);if(Style.ColorField("Glow color",ref glow)){initialized=true;savedTitle="";}ImGui.EndDisabled();
        if(ImGui.Checkbox("Place title before character name",ref prefix)){initialized=true;savedTitle="";}
        Style.InputInt("Duration minutes (0 = until cleared)",ref titleMinutes);EditingText|=ImGui.IsItemActive();
        ImGui.BeginDisabled(blocked||!available||string.IsNullOrWhiteSpace(title)||!IntegrationCatalog.Minutes(titleMinutes));
        if(Style.PrimaryButton("Apply title",new Vector2(-1,40)))Send(session,"honorific",new{characterId=target,operation="set",title=title.Trim(),colour=color,glow=glowOn?glow:"",prefix,durationMinutes=titleMinutes});
        ImGui.EndDisabled();
        ImGui.BeginDisabled(blocked||!available);
        if(ImGui.Button("Clear PetService title",new Vector2(-1,36)))Send(session,"honorific",new{characterId=target,operation="clear"});
        ImGui.EndDisabled();
        ImGui.TextWrapped(blocked?"Controls are paused or this character is not approved.":!available?"Honorific is unavailable in the last report.":!IntegrationCatalog.Minutes(titleMinutes)?"Enter a whole number from 0 to 10,080 minutes.":"Apply a saved title or enter your own text.");
        Style.EndCard();
    }
    private void DrawMoodles(MasterPortalSession session,bool blocked,bool available,JsonElement moodles,List<JsonElement> statuses,List<JsonElement> presets,List<JsonElement> current,List<JsonElement> removable,HashSet<string> managed)
    {
        Style.BeginCard("moodles","Moodles","Apply statuses and presets, or remove PetService-managed statuses");
        Availability(available,PortalJson.Text(moodles,"version"));
        Select("Status",ref statusId,statuses,"Choose a status…",true);
        var status=statuses.FirstOrDefault(s=>PortalJson.Text(s,"id")==statusId);
        if(status.ValueKind==JsonValueKind.Object)Preview(status);else ImGui.TextWrapped("Select a status to see its icon and description.");
        Style.InputInt("Duration minutes (0 = configured)",ref moodleMinutes);EditingText|=ImGui.IsItemActive();
        Style.InputInt("Stacks (0 = configured)",ref stacks);EditingText|=ImGui.IsItemActive();
        var settingsValid=IntegrationCatalog.Minutes(moodleMinutes)&&stacks is >=0 and <=99;
        ImGui.BeginDisabled(blocked||!available||!IntegrationCatalog.SafeStatus(status)||!settingsValid);
        if(Style.PrimaryButton("Apply status",new Vector2(-1,40)))Send(session,"moodles",new{characterId=target,operation="apply",statusId,durationMinutes=moodleMinutes,stacks=stacks>0?(int?)stacks:null});
        ImGui.EndDisabled();
        ImGui.TextWrapped(!available?"Moodles is unavailable in the last report.":statusId.Length==0?"Choose a saved status.":!IntegrationCatalog.SafeStatus(status)?"This status is persistent, restricted, or chained and cannot be applied through PetService.":!settingsValid?"Duration must be 0–10,080 minutes; stacks must be 0–99.":"The request is confirmed after the pet’s plugin applies it.");
        ImGui.Separator();Select("Preset",ref presetId,presets,"Choose a preset…");
        var preset=presets.FirstOrDefault(p=>PortalJson.Text(p,"id")==presetId);
        foreach(var id in PortalJson.Array(preset,"statuses")) {
            var preview=statuses.FirstOrDefault(s=>PortalJson.Text(s,"id")==PortalJson.String(id));
            if(preview.ValueKind==JsonValueKind.Object)Preview(preview);else ImGui.TextWrapped("A preset status is unavailable in this catalog.");
        }
        ImGui.BeginDisabled(blocked||!available||!IntegrationCatalog.SafePreset(preset,moodles)||!settingsValid);
        if(ImGui.Button("Apply preset statuses",new Vector2(-1,36)))Send(session,"moodles",new{characterId=target,operation="preset",preset=presetId,durationMinutes=moodleMinutes,stacks=stacks>0?(int?)stacks:null});
        ImGui.EndDisabled();
        if(presetId.Length>0&&!IntegrationCatalog.SafePreset(preset,moodles))ImGui.TextWrapped("This preset is empty or includes a status PetService cannot apply.");
        ImGui.Separator();ImGui.TextUnformatted("Remove managed statuses");
        Select("Managed status",ref removeId,removable,"Choose a managed status…");
        ImGui.BeginDisabled(blocked||!available||removeId.Length==0);
        if(ImGui.Button("Remove selected status",new Vector2(-1,36)))Send(session,"moodles",new{characterId=target,operation="remove",statusId=removeId});ImGui.EndDisabled();
        ImGui.BeginDisabled(blocked||!available||removable.Count==0);
        if(ImGui.Button("Clear all PetService-managed statuses",new Vector2(-1,36)))Send(session,"moodles",new{characterId=target,operation="clear"});ImGui.EndDisabled();
        if(ImGui.CollapsingHeader("Current statuses")) {
            foreach(var item in current){Preview(item);ImGui.TextColored(Style.Muted,managed.Contains(PortalJson.Text(item,"id"))?"Managed by PetService":"Not managed by PetService");ImGui.Separator();}
            if(current.Count==0)ImGui.TextWrapped(available?"No active statuses in the last report.":"Current statuses are unavailable.");
        }
        Style.EndCard();
    }
    private static void Select(string label,ref string id,List<JsonElement> choices,string placeholder,bool restricted=false)
    {
        var selected=id;var choice=choices.FirstOrDefault(c=>PortalJson.Text(c,"id")==selected);
        if(!Style.BeginCombo(label,id.Length>0?PortalJson.Text(choice,"title"):placeholder))return;
        if(ImGui.Selectable(placeholder,id.Length==0))id="";
        foreach(var item in choices) {
            var key=PortalJson.Text(item,"id");ImGui.PushID(key);
            if(ImGui.Selectable(PortalJson.Text(item,"title")+(restricted&&!IntegrationCatalog.SafeStatus(item)?" · Restricted":""),key==id))id=key;
            ImGui.PopID();
        }
        ImGui.EndCombo();
    }
    private static void Availability(bool available,string version)=>ImGui.TextColored(available?Style.Good:Style.Muted,available?"Available"+(version.Length>0?" · "+version:""):"Unavailable");
    private static void Preview(JsonElement item)
    {
        var icon=PortalJson.Number(item,"iconId");
        try {
            var texture=icon>0&&icon<=uint.MaxValue?Plugin.Textures.GetFromGameIcon(new GameIconLookup((uint)icon)).GetWrapOrDefault():null;
            if(texture is not null){ImGui.Image(texture.Handle,new Vector2(40));ImGui.SameLine();}
        } catch { }
        ImGui.TextWrapped(PortalJson.Text(item,"title")+(PortalJson.Get(item,"stacks").ValueKind==JsonValueKind.Number?" · Stacks "+PortalJson.Text(item,"stacks"):""));
        if(PortalJson.Text(item,"description") is {Length:>0} description)ImGui.TextWrapped(description);
    }
}
