using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using PetService.Core;

namespace PetService.Windows;

internal sealed class MasterPortalView : IDisposable
{
    private readonly FeaturePortalView features=new();
    private readonly JournalPortalView journal=new();
    private readonly TaskEvidenceView evidence=new();
    private string category="Now";private bool addRequested;
    private readonly Dictionary<string,string> lastPages=[];
    private string masterPage="Overview";
    private readonly MasterPortalSession session=new(new MasterPortalClient());
    private string password="",newPet="",filter="";
    private string pairingCode="",pairingPet="",pairingExpiry="",confirmationText="";
    private bool hadAccess,confirmRequested;
    private string deleteName="",templateName="";
    private readonly DynamicSettingsEditor settingsEditor=new();
    private long reloadAfterRead;
    private bool reloadSettings;
    private sealed class ChatDraft {internal string Text="";internal int Expiry;}
    private readonly Dictionary<string,ChatDraft> chatDrafts=[];
    private AdminAction? confirmation;
    private sealed class Draft {internal string Text="",Choices="";internal bool AllowReply=true;}
    private readonly Dictionary<string,Draft> drafts=[];
    internal bool EditingText { get; private set; }

    internal void Draw()
    {
        EditingText=false;session.Update();
        if(hadAccess && !session.Unlocked){journal.Dispose();evidence.Dispose();features.Clear();ClearFields();}
        hadAccess=session.Unlocked;CompleteAction();features.Begin(session);
        if(!session.Unlocked) {
            Style.BeginCard("master-login","Master portal","Enter your administrator password to manage pets");
            ImGui.BeginDisabled(session.Busy);Input("Administrator password",ref password,4096,true);
            ImGui.BeginDisabled(password.Length==0);
            if(Style.PrimaryButton("Unlock Master portal",new Vector2(-1,42))){session.Unlock(password);password="";}
            ImGui.EndDisabled();ImGui.EndDisabled();ImGui.TextWrapped("Closing the window or locking clears this session’s password.");
            DrawFeedback();Style.EndCard();return;
        }
        var dashboard=session.Dashboard!;Style.ApplyTheme(dashboard.Identity.Theme);
        ImGui.BeginDisabled(session.Busy);
        if(Style.PrimaryButton("Request fresh report",new Vector2(220,42))) {
            if(dashboard.Selected is {Paired:true} pet)session.Action(new(){Action="requestReport",Pet=pet.Name});else session.Refresh();
        }
        ImGui.EndDisabled();ImGui.SameLine();if(ImGui.Button("Lock portal",new Vector2(130,42))){Lock();return;}
        var auto=session.AutoRefresh;if(ImGui.Checkbox("Live updates while viewing Overview",ref auto))session.AutoRefresh=auto;
        ImGui.TextColored(Style.Muted,$"Service {(dashboard.Service.DatabaseReady ? "connected" : "unavailable")} · Discord {(dashboard.Service.DiscordReady ? "connected" : "offline")}");DrawFeedback();
        if(ImGui.BeginTable("master-profiles",2,ImGuiTableFlags.SizingStretchProp)) {
            ImGui.TableSetupColumn("Select",ImGuiTableColumnFlags.WidthStretch,3);ImGui.TableSetupColumn("Add",ImGuiTableColumnFlags.WidthStretch,1);
            ImGui.TableNextColumn();ImGui.BeginDisabled(session.Busy);
            ImGui.SetNextItemWidth(-1);
            if(ImGui.BeginCombo("##PetProfile",session.Selected.Length>0?session.Selected:"Choose a pet")) {
                Input("Find a pet",ref filter,32);
                foreach(var pet in dashboard.Profiles.Where(p=>p.Name.Contains(filter,StringComparison.OrdinalIgnoreCase)))if(ImGui.Selectable(pet.Name+" · "+MasterPortalPolicy.State(pet,session.Now),pet.Name==session.Selected)) {
                    pairingCode=pairingPet="";reloadSettings=false;journal.Dispose();evidence.Dispose();session.Select(pet.Name);
                }
                ImGui.EndCombo();
            }
            ImGui.EndDisabled();ImGui.TableNextColumn();ImGui.BeginDisabled(session.Busy);
            if(ImGui.Button("Add pet",new Vector2(-1,ImGui.GetFrameHeight())))addRequested=true;ImGui.EndDisabled();ImGui.EndTable();
        }
        DrawNewPet();
        var detail=dashboard.Selected;
        if(detail is null||detail.Name!=session.Selected){ImGui.TextWrapped(dashboard.Profiles.Count==0?"Add a pet profile, then generate a pairing code on Profile to connect their plugin.":"Choose a profile to open its Master controls.");session.Hide();return;}
        ImGui.TextColored(Style.Muted,detail.Name+" · "+MasterPortalPolicy.State(detail,session.Now));
        DrawNavigation();
        var viewingOverview=false;var viewingChats=false;var viewingSettings=false;var settingsSection="controls";var settingsCharacter="";
        ImGui.BeginDisabled(session.Busy);
        switch(masterPage) {
            case "Overview":
                viewingOverview=true;DrawStatus(detail);DrawOverview(detail);
                Style.BeginCard("overview-tasks","Tasks awaiting attention","Pending assignments and submissions");
                var dueTasks=detail.Features.Tasks.Where(t=>t.State is "pending" or "in-progress" or "submitted").ToList();
                foreach(var task in dueTasks.Take(5))ImGui.TextWrapped(task.Title+" · "+PortalPresentation.State(task.State)+(task.DueAtUtc is not null?" · Due "+LocalDate(task.DueAtUtc):""));
                if(dueTasks.Count==0)ImGui.TextColored(Style.Muted,"No tasks awaiting attention.");
                if(ImGui.Button("Open Tasks"))Navigate("Tasks");Style.EndCard();DrawAttention(detail);break;
            case "Inventory":viewingSettings=true;settingsSection="inventory";features.Inventory(session,detail);settingsCharacter=features.InventoryCharacter;break;
            case "Tasks":viewingSettings=true;settingsSection="task-evidence";features.Tasks(session,detail,id=>{evidence.Open(id);Navigate("Task evidence");});break;
            case "Task evidence":viewingSettings=true;settingsSection="task-evidence";evidence.Draw(session,detail,id=>{features.FocusTask(id);Navigate("Tasks");});EditingText|=evidence.EditingText;break;
            case "Bonding":viewingSettings=true;features.Controls(session,detail);break;
            case "Integrations":viewingSettings=true;settingsSection="integrations";features.Integrations(session,detail);settingsCharacter=features.IntegrationCharacter;break;
            case "Master identity":viewingSettings=true;features.Identity(session);break;
            case "Rewards":viewingSettings=true;settingsSection="rewards";features.Rewards(session,detail);break;
            case "Chat messages":viewingChats=true;DrawChat(detail);break;
            case "Reminders":features.Reminders(session,detail);break;
            case "Messages":ImGui.BeginDisabled(FeaturePortalView.Paused(detail));DrawMessage(detail);ImGui.EndDisabled();DrawPending(detail);break;
            case "Settings":viewingSettings=true;DrawDynamic(detail);break;
            case "Profile":DrawProfile(detail);break;
            case "Travel":DrawTravelTotals(detail);goto default;
            default:journal.Draw(session,detail,masterPage);EditingText|=journal.EditingText;break;
        }
        EditingText|=features.EditingText;ImGui.EndDisabled();DrawConfirmation();
        if(!ImGui.GetIO().AppFocusLost)session.TickVisible(viewingOverview,viewingChats,viewingSettings,settingsSection,settingsCharacter);else session.Hide();
    }
    private void Navigate(string page)
    {
        if(page==masterPage)return;
        lastPages[category]=masterPage;category=PortalPresentation.Navigation.First(g=>g.Pages.Contains(page)).Category;
        if(masterPage=="Task evidence")evidence.Hide();journal.Dispose();masterPage=page;lastPages[category]=page;
    }
    private void DrawNavigation()
    {
        ImGui.Spacing();Buttons(PortalPresentation.Navigation.Select(g=>g.Category),category,chosen=>Navigate(lastPages.GetValueOrDefault(chosen)??PortalPresentation.Navigation.First(g=>g.Category==chosen).Pages[0]));
        Buttons(PortalPresentation.Navigation.First(g=>g.Category==category).Pages,masterPage,Navigate);
        ImGui.Separator();ImGui.Spacing();
        ImGui.SetWindowFontScale(1.2f);ImGui.TextUnformatted(masterPage);ImGui.SetWindowFontScale(1);ImGui.Spacing();
    }
    private static void Buttons(IEnumerable<string> labels,string selected,Action<string> click)
    {
        var width=ImGui.GetContentRegionAvail().X;var used=0f;var spacing=ImGui.GetStyle().ItemSpacing.X;
        foreach(var label in labels) {
            var size=ImGui.CalcTextSize(label).X+ImGui.GetStyle().FramePadding.X*2;
            if(used>0&&used+spacing+size<=width){ImGui.SameLine();used+=spacing;}else used=0;
            var active=label==selected;
            if(active){ImGui.PushStyleColor(ImGuiCol.Button,Style.Accent);ImGui.PushStyleColor(ImGuiCol.Text,Style.AccentText);}
            if(ImGui.Button(label))click(label);if(active)ImGui.PopStyleColor(2);used+=size;
        }
    }
    private void DrawNewPet()
    {
        const string id="Add pet profile###MasterNewPet";
        if(addRequested){ImGui.OpenPopup(id);addRequested=false;newPet="";}
        ImGui.SetNextWindowSize(new Vector2(480,300)*ImGuiHelpers.GlobalScale,ImGuiCond.Appearing);
        if(!ImGui.BeginPopupModal(id,ImGuiWindowFlags.NoResize|ImGuiWindowFlags.NoSavedSettings))return;
        Input("Pet profile name",ref newPet,32);
        ImGui.TextWrapped("Use a lowercase name with letters, numbers, hyphens, or underscores.");
        ImGui.BeginDisabled(session.Busy||!MasterPortalPolicy.ValidName(newPet));
        if(ImGui.Button("Create profile")){session.Action(new(){Action="createPet",Name=newPet});ImGui.CloseCurrentPopup();}ImGui.EndDisabled();ImGui.SameLine();
        if(ImGui.Button("Cancel"))ImGui.CloseCurrentPopup();ImGui.EndPopup();
    }
    private void DrawFeedback()
    {
        if(session.Busy)ImGui.TextUnformatted("Connecting…");
        if(session.Error.Length>0)ImGui.TextWrapped(session.Error);
        if(session.Notice.Length>0)ImGui.TextWrapped(session.Notice);
        if(session.CanRetry && ImGui.Button("Retry last action"))session.Retry();
    }
    private void DrawStatus(AdminPet pet)
    {
        if(pet.Status.State=="unapproved"){Style.Title("Unapproved character","Pet is on an unapproved alt. Live reporting and Master controls are paused.");return;}
        var status=pet.Status.Reported;var fresh=MasterPortalPolicy.Fresh(pet,session.Now);
        Style.Title("Character status","Current reports while this Overview is visible");
        if(ImGui.BeginTable("liveidentity",2,ImGuiTableFlags.SizingStretchSame)) {
            ImGui.TableNextColumn();Style.Metric("Character",status?.CharacterName is {Length:>0} name?name:"Not observed","livecharacter");
            ImGui.TableNextColumn();Style.Metric("Connection",MasterPortalPolicy.State(pet,session.Now),"liveconnection");ImGui.EndTable();
        }
        ImGui.TextWrapped("Last contact: "+LocalDate(pet.Status.LastSeenAtUtc));
        ImGui.TextColored(Style.Muted,"Report received: "+LocalDate(pet.Status.SnapshotAtUtc ?? pet.Status.LastSeenAtUtc));
        if(status is null){ImGui.TextWrapped("Awaiting a character report. Use Refresh now to request one.");return;}
        var job=status.Job.Length>0?CultureInfo.CurrentCulture.TextInfo.ToTitleCase(status.Job):"Unknown job";
        var duration=fresh && status.LoggedIn && DateTimeOffset.TryParse(status.LoginObservedAtUtc,out var login)?Duration(session.Now-login):"Unknown";
        if(ImGui.BeginTable("livejob",2,ImGuiTableFlags.SizingStretchSame)) {
            ImGui.TableNextColumn();Style.Metric("Job and level",job+" · Level "+status.Level,"livejobname");
            ImGui.TableNextColumn();Style.Metric("Observed session",duration,"livesession");ImGui.EndTable();
        }
        ImGui.TextWrapped((status.LoginTimeSource=="login-event"?"Login observed: ":"First observed: ")+LocalDate(status.LoginObservedAtUtc));
        ImGui.Spacing();ImGui.Separator();Style.Title("Location",fresh?"Latest character report":"Last observed location");
        if(ImGui.BeginTable("livelocation",2,ImGuiTableFlags.SizingStretchSame)) {
            ImGui.TableNextColumn();Style.Metric("Area",status.Zone.Length>0?status.Zone:"Not observed","livearea");
            ImGui.TableNextColumn();Style.Metric("Coordinates",status.X is not null && status.Y is not null?$"X {status.X:F2} · Y {status.Y:F2}":"Unknown","livecoordinates");ImGui.EndTable();
        }
        ImGui.TextWrapped("World: "+status.CurrentWorld+" · Data Center: "+status.DataCenter);
        ImGui.TextWrapped("Home world: "+status.HomeWorld);
        ImGui.TextColored(Style.Muted,$"Territory ID: {status.TerritoryId} · Map ID: {status.MapId}");
        ImGui.Spacing();ImGui.Separator();
        if(!fresh)ImGui.TextWrapped("Current state is unknown. The states below are from the last report.");
        if(ImGui.BeginTable("livegroups",2,ImGuiTableFlags.SizingStretchSame)) {
            ImGui.TableNextColumn();
            Style.StateGroup("presenceflags","Character status",fresh,[
                ("Character ready",status.Ready?"Ready":"Not ready",status.Ready),
                ("Away from keyboard",status.IsAfk?"Away":"Present",status.IsAfk),
                ("Game activity",status.GameIdle?"Idle":"Active",!status.GameIdle),
                ("Consciousness",status.Unconscious?"Unconscious":"Conscious",status.Unconscious)],status.LoggedIn);
            ImGui.TableNextColumn();
            Style.StateGroup("activityflags","Current activity",fresh,[
                ("Duty",status.InDuty?"In duty":"Outside duty",status.InDuty),
                ("Combat",status.InCombat?"In combat":"Out of combat",status.InCombat),
                ("Crafting",status.Crafting?"Active":"Inactive",status.Crafting),
                ("Gathering",status.Gathering?"Active":"Inactive",status.Gathering),
                ("Mount",status.Mounted?"Mounted":"On foot",status.Mounted),
                ("Cutscene",status.Cutscene?"Watching":"Inactive",status.Cutscene)],status.LoggedIn);
            ImGui.EndTable();
        }
        ImGui.Spacing();Style.StateGroup("controlflags","Plugin controls",fresh,[
            ("Gameplay blocked",status.InputGuardActive?"On":"Off",status.InputGuardActive),
            ("Safe mode",status.LocalRelease?"On":"Off",status.LocalRelease)]);
    }
    private void DrawChat(AdminPet pet)
    {
        Style.Title("Master chat messages","Local chat delivery and message status");
        ImGui.TextWrapped("The pet chooses Echo or System output and uses their FFXIV chat filters. Displayed confirms the plugin queued the entry into chat; it does not confirm reading.");
        if(!chatDrafts.TryGetValue(pet.Name,out var draft)){draft=new();chatDrafts[pet.Name]=draft;}
        ImGui.InputTextMultiline("##MasterChatBody",ref draft.Text,1000,new Vector2(-1,90));EditingText|=ImGui.IsItemActive();
        Style.InputInt("Expire after minutes (0 = no expiry)",ref draft.Expiry);
        ImGui.BeginDisabled(pet.Archived || string.IsNullOrWhiteSpace(draft.Text) || draft.Expiry is <0 or >10080);
        if(ImGui.Button("Send chat message"))session.Action(new(){Action="sendChat",Pet=pet.Name,Text=draft.Text.Trim(),ExpiresMinutes=draft.Expiry});
        ImGui.EndDisabled();ImGui.SameLine();if(ImGui.Button("Reload delivery status"))session.Refresh();
        ImGui.TextWrapped("["+(session.Dashboard?.Identity.Name??"Master")+"] "+draft.Text);
        foreach(var message in pet.ChatMessages) {
            ImGui.PushID(message.Id);ImGui.Separator();ImGui.TextColored(Style.Accent,ActivityLabels.Field(message.State));ImGui.TextWrapped(message.Text);
            ImGui.TextColored(Style.Muted,"Created "+LocalDate(message.CreatedAtUtc));
            if(message.ExpiresAtUtc is not null)ImGui.TextUnformatted("Expires "+LocalDate(message.ExpiresAtUtc));
            if(message.DisplayedAtUtc is not null)ImGui.TextUnformatted("Displayed "+LocalDate(message.DisplayedAtUtc));
            if(message.State=="queued" && ImGui.SmallButton("Cancel chat message…"))Confirm(new(){Action="cancelChat",Pet=pet.Name,MessageId=message.Id},"Cancel delivery of this queued Master chat message?");
            ImGui.PopID();
        }
        if(pet.ChatMessages.Count==0)ImGui.TextColored(Style.Muted,"No Master chat messages yet.");
    }
    private void DrawTravelTotals(AdminPet pet)
    {
        Style.Title("Travel history","Completed arrivals, including same-zone transfers");
        var stats=pet.TravelStats;
        if(ImGui.BeginTable("travelmetrics",4)) {
            foreach(var (label,count) in new[]{("Today",stats.Today),("This week",stats.Week),("This month",stats.Month),("Lifetime",stats.Total)}) {ImGui.TableNextColumn();Style.Metric(label,count.ToString(),"travel"+label);}
            ImGui.EndTable();
        }
        ImGui.TextWrapped("Counts include recorded area/zone transfers. An unidentified transfer stays unidentified; ordinary movement is not a trip.");
        foreach(var (label,groups) in new[]{("Travel methods",stats.Methods),("Frequent destinations",stats.Destinations),("Travel by character",stats.Characters)})if(ImGui.CollapsingHeader(label))foreach(var row in groups)ImGui.TextWrapped((label=="Travel methods"?ActivityLabels.TravelMethod(row.Label):row.Label)+" · "+row.Trips);
        if(ImGui.Button("Reload travel totals"))session.Refresh();
    }
    private void DrawMessage(AdminPet pet)
    {
        if(!ImGui.CollapsingHeader("Send a message",ImGuiTreeNodeFlags.DefaultOpen))return;
        if(!drafts.TryGetValue(pet.Name,out var draft)){draft=new();drafts[pet.Name]=draft;}
        var scale=ImGuiHelpers.GlobalScale;
        ImGui.TextUnformatted("Message");
        ImGui.InputTextMultiline("##MasterMessage",ref draft.Text,1000,new Vector2(-1,85*scale));EditingText|=ImGui.IsItemActive();
        ImGui.TextUnformatted("Reply buttons");
        ImGui.InputTextMultiline("##MasterReplyButtons",ref draft.Choices,2591,new Vector2(-1,70*scale));EditingText|=ImGui.IsItemActive();
        ImGui.TextWrapped("Optional: one button per line, up to 32 buttons with 80 characters each.");
        ImGui.Checkbox("Allow a written reply",ref draft.AllowReply);
        if(pet.Settings.Templates.Count>0 && Style.BeginCombo("Saved template","Choose…")) {
            foreach(var template in pet.Settings.Templates)if(ImGui.Selectable(template.Name)){draft.Text=template.Text;draft.Choices=string.Join('\n',template.Choices);draft.AllowReply=template.AllowReply;}
            ImGui.EndCombo();
        }
        var error=MasterPortalPolicy.ChoiceError(draft.Choices,draft.AllowReply,out var labels);
        if(error is not null)ImGui.TextWrapped(error);
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(draft.Text) || error is not null);
        if(ImGui.Button("Send prompt"))session.Action(new AdminAction{Action="sendMessage",Pet=pet.Name,Text=draft.Text.Trim(),Choices=labels,AllowReply=draft.AllowReply});
        ImGui.EndDisabled();
        if(!ImGui.CollapsingHeader("Save as a template"))return;
        Input("Template name",ref templateName,60);
        ImGui.BeginDisabled(templateName.Trim().Length==0 || string.IsNullOrWhiteSpace(draft.Text) || error is not null || pet.Settings.Templates.Count>=20);
        if(ImGui.Button("Save template")) {
            var config=Clone(pet.Settings);config.Templates.RemoveAll(t=>t.Name==templateName.Trim());config.Templates.Add(new(){Name=templateName.Trim(),Text=draft.Text,Choices=labels,AllowReply=draft.AllowReply});
            session.Action(new(){Action="saveSettings",Pet=pet.Name,Settings=config});
        }
        ImGui.EndDisabled();
        if(ImGui.TreeNode("Preview prompt")){ImGui.TextColored(Style.Accent,"Message from Master");ImGui.TextWrapped(draft.Text);foreach(var label in labels)ImGui.Button(label+"##preview");ImGui.TreePop();}
    }
    private void DrawPending(AdminPet pet)
    {
        if(!ImGui.CollapsingHeader("Pending prompts"))return;
        foreach(var prompt in pet.Pending) {
            ImGui.PushID(prompt.Id);ImGui.Separator();
            ImGui.TextUnformatted(prompt.Kind=="message" ? "Message from Master" : prompt.Label);
            if(prompt.Text.Length>0)ImGui.TextWrapped(prompt.Text);
            if(prompt.Choices.Count>0)ImGui.TextWrapped("Reply buttons: "+string.Join(" · ",prompt.Choices));
            if(prompt.Kind=="message" && !prompt.AllowReply)ImGui.TextUnformatted("Reply buttons only");
            ImGui.TextUnformatted(prompt.DisplayedAtUtc is null ? "Waiting to be shown" : "Shown in game");
            ImGui.TextUnformatted(prompt.SnoozedUntilUtc is not null ? "Snoozed until "+LocalDate(prompt.SnoozedUntilUtc) : "Due "+LocalDate(prompt.DueAtUtc));
            if(ImGui.Button("Cancel prompt…"))Confirm(new AdminAction{Action="cancelPrompt",Pet=pet.Name,PromptId=prompt.Id},
                "Remove this pending prompt without recording a reply?");
            ImGui.PopID();
        }
        if(pet.Pending.Count==0)ImGui.TextUnformatted("No pending prompts.");
    }
    private static DynamicSettings Clone(DynamicSettings value)=>System.Text.Json.JsonSerializer.Deserialize<DynamicSettings>(System.Text.Json.JsonSerializer.Serialize(value,ServiceClient.Json),ServiceClient.Json)!;
    private static void DrawOverview(AdminPet pet)
    {
        Style.Title("Playtime and prompts",pet.Archived?"Archived profile · history retained":"Recorded totals for this pet");
        if(ImGui.BeginTable("metrics",3)) {
            ImGui.TableNextColumn();Style.Metric("Observed today",Duration(TimeSpan.FromSeconds(pet.Stats.TodaySeconds)),"today");
            ImGui.TableNextColumn();Style.Metric("Observed lifetime",Duration(TimeSpan.FromSeconds(pet.Stats.TotalSeconds)),"lifetime");
            ImGui.TableNextColumn();Style.Metric("Awaiting a reply",pet.Pending.Count.ToString(),"pending");ImGui.EndTable();
        }
    }
    private void DrawAttention(AdminPet pet)
    {
        Style.BeginCard("attention","Attention requests","Contact requests awaiting your response");
        foreach(var item in pet.Attention.Where(a=>a.HandledAtUtc is null)) {
            ImGui.PushID(item.Id);ImGui.TextWrapped(item.Text);ImGui.TextColored(Style.Muted,LocalDate(item.CreatedAtUtc));
            if(ImGui.Button("Mark handled"))session.Action(new(){Action="handleAttention",Pet=pet.Name,EventId=item.Id});
            ImGui.SameLine();if(ImGui.Button("Prepare reply")){if(!drafts.ContainsKey(pet.Name))drafts[pet.Name]=new();drafts[pet.Name].Text="I'm here. What do you need?";Navigate("Messages");}
            ImGui.PopID();
        }
        if(!pet.Attention.Any(a=>a.HandledAtUtc is null))ImGui.TextColored(Style.Muted,"No requests waiting.");Style.EndCard();
    }
    private void DrawDynamic(AdminPet pet)
    {
        var discard=reloadSettings && session.SettingsReadCount>reloadAfterRead;
        settingsEditor.Receive(pet.Name,pet.Settings,discard);
        if(discard)reloadSettings=false;
        var editSettings=settingsEditor.Value;
        Style.Title("Speech and contact settings","Gagging and contact presets for this pet");
        var garble=editSettings.GarbleEnabled;if(ImGui.Checkbox("Gag outgoing speech",ref garble))editSettings.GarbleEnabled=garble;
        var strength=editSettings.GarbleStrength;if(ImGui.SliderInt("Strength",ref strength,1,100))editSettings.GarbleStrength=strength;
        if(Style.BeginCombo("Style",editSettings.GarbleStyle)) {foreach(var name in new[]{"muffled","soft","playful"})if(ImGui.Selectable(name))editSettings.GarbleStyle=name;ImGui.EndCombo();}
        ImGui.TextWrapped("Transformations happen locally. Incoming chat, commands and Pet Service replies are untouched. The pet's kill switch disables gagging.");
        ImGui.TextUnformatted("Contact presets · one per line in display order");
        ImGui.InputTextMultiline("##contacts",ref settingsEditor.Contacts,2591,new Vector2(-1,145));EditingText|=ImGui.IsItemActive();
        var error=MasterPortalPolicy.ChoiceError(settingsEditor.Contacts,true,out var contacts);
        if(error is not null)ImGui.TextWrapped(error);
        ImGui.BeginDisabled(error is not null || pet.Archived);
        if(ImGui.Button("Save settings")){reloadSettings=false;editSettings.Contacts=contacts;session.Action(new(){Action="saveSettings",Pet=pet.Name,Settings=Clone(editSettings)});}
        ImGui.EndDisabled();
        if(ImGui.Button("Reload saved settings")){reloadSettings=true;reloadAfterRead=session.SettingsReadCount;session.RefreshSettings();}
        ImGui.TextColored(Style.Muted,reloadSettings?"Loading saved settings…":settingsEditor.SavedChanged?"Saved settings changed elsewhere. Your unsaved edits are preserved.":settingsEditor.Dirty?"Unsaved changes · select Save settings to apply.":"Saved settings · synchronized automatically while this page is visible.");
        foreach(var template in editSettings.Templates.ToArray()){ImGui.PushID(template.Name);ImGui.TextUnformatted(template.Name);ImGui.SameLine();if(ImGui.SmallButton("Remove template")){editSettings.Templates.Remove(template);}ImGui.PopID();}
    }
    private void DrawProfile(AdminPet pet)
    {
        Style.Title("Profile management",pet.Archived?"Archived":"Active");
        Style.BeginCard("device-pairing","Device pairing","One-time codes connect the pet’s plugin to this profile");
        ImGui.BeginDisabled(pet.Archived);
        if(ImGui.Button("Generate pairing code")){pairingCode="";session.Action(new(){Action="pair",Pet=pet.Name});}ImGui.EndDisabled();
        if(ImGui.Button("Revoke device…"))Confirm(new(){Action="revoke",Pet=pet.Name},"Disconnect this device and clear its unused pairing code? A new pairing is required to reconnect.");
        if(pairingCode.Length>0&&pairingPet==pet.Name) {
            ImGui.TextUnformatted("Pairing code");ImGui.SetNextItemWidth(-1);ImGui.InputText("##MasterPairingCode",ref pairingCode,80,ImGuiInputTextFlags.ReadOnly);
            if(ImGui.Button("Copy pairing code"))ImGui.SetClipboardText(pairingCode);ImGui.SameLine();if(ImGui.Button("Clear code"))pairingCode="";
            ImGui.TextWrapped("Expires "+LocalDate(pairingExpiry)+" · Single use");
        }
        Style.EndCard();
        Style.BeginCard("profile-lifecycle","Profile management",pet.Archived?"Archived profile":"Active profile");
        if(ImGui.Button(pet.Archived?"Restore profile…":"Archive profile…"))Confirm(new(){Action=pet.Archived?"restorePet":"archivePet",Pet=pet.Name},pet.Archived?"Restore this profile? Generate a new pairing code afterward.":"Archive and revoke access? History stays saved; pending prompts are canceled.");
        if(ImGui.Button("Permanently delete profile…")){deleteName="";Confirm(new(){Action="deletePet",Pet=pet.Name},"Delete this pet, pairing, reminders, prompts and service history permanently? Already delivered Discord messages remain. Type the profile name to confirm.");}
        Style.EndCard();
    }
    private void Input(string label,ref string value,int max,bool secret=false)
    {
        ImGui.TextUnformatted(label);ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##"+label,ref value,max,secret?ImGuiInputTextFlags.Password:ImGuiInputTextFlags.None);EditingText|=ImGui.IsItemActive();
    }
    private void Confirm(AdminAction action,string text){confirmation=action;confirmationText=text;confirmRequested=true;}
    private void DrawConfirmation()
    {
        const string id="Confirm master action###PetServiceMasterAction";
        if(confirmRequested){ImGui.OpenPopup(id);confirmRequested=false;}
        var open=true;
        if(ImGui.BeginPopupModal(id,ref open,ImGuiWindowFlags.AlwaysAutoResize)) {
            ImGui.TextWrapped(confirmationText);
            if(confirmation?.Action=="deletePet")Input("Type profile name",ref deleteName,32);
            ImGui.BeginDisabled(session.Busy || confirmation is null || (confirmation.Action=="deletePet" && deleteName!=confirmation.Pet));
            if(ImGui.Button("Confirm")){session.Action(confirmation!.Action=="deletePet"?confirmation with{Confirmation=deleteName}:confirmation);confirmation=null;deleteName="";ImGui.CloseCurrentPopup();}
            ImGui.EndDisabled();ImGui.SameLine();
            if(ImGui.Button("Keep it")){confirmation=null;ImGui.CloseCurrentPopup();}
            ImGui.EndPopup();
        }
        if(!open)confirmation=null;
    }
    private void CompleteAction()
    {
        var action=session.CompletedAction;var result=session.CompletedResult;
        if(action is null || result is null)return;
        features.Completed(action);
        if(action.Action=="saveSettings" && settingsEditor.Pet==action.Pet) {
            settingsEditor.Receive(action.Pet!,result.Settings ?? action.Settings!,true);reloadSettings=false;
        }
        if(action.Action=="deletePet" || action.Action=="archivePet"){settingsEditor.Clear();reloadSettings=false;}
        if(action.Action=="createPet")newPet="";
        if(action.Action=="sendMessage" && action.Pet is not null)drafts.Remove(action.Pet);
        if(action.Action=="sendChat" && action.Pet is not null)chatDrafts.Remove(action.Pet);
        if(action.Action=="pair"){pairingCode=result.Code ?? "";pairingPet=result.Pet;pairingExpiry=result.ExpiresAtUtc ?? "";}
        session.ClearCompletion();
    }
    private static string LocalDate(string? value)=>PortalPresentation.Date(value);
    private static string Duration(TimeSpan span)=>$"{Math.Max(0,(int)span.TotalHours)}h {Math.Max(0,span.Minutes)}m";
    internal void Hide(){session.Hide();journal.Dispose();evidence.Hide();}
    internal void ClearTextFocus()=>EditingText=false;
    private void ClearFields()
    {
        password="";newPet="";filter="";pairingCode="";pairingPet="";pairingExpiry="";
        confirmation=null;confirmationText="";confirmRequested=addRequested=false;drafts.Clear();chatDrafts.Clear();EditingText=false;settingsEditor.Clear();reloadSettings=false;deleteName=templateName="";
    }
    internal void Lock(){journal.Dispose();evidence.Dispose();features.Clear();session.Lock();ClearFields();hadAccess=false;}
    public void Dispose(){journal.Dispose();evidence.Dispose();features.Dispose();ClearFields();session.Dispose();}
}
