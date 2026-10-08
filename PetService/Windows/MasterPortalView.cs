using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using PetService.Core;

namespace PetService.Windows;

internal sealed class MasterPortalView : IDisposable
{
    private readonly FeaturePortalView features=new();
    private readonly MasterPortalSession session=new(new MasterPortalClient());
    private string password="",newPet="",filter="",reminderLabel="",reminderTime="18:00";
    private string pairingCode="",pairingPet="",pairingExpiry="",confirmationText="";
    private bool hadAccess,confirmRequested;
    private string historyFrom="",historyTo="",historyPageKind="",statFrom="",statTo="";
    private string timelineFilter="",deleteName="",templateName="";
    private string travelMethod="";
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
        if(hadAccess && !session.Unlocked){features.Clear();ClearFields();}
        hadAccess=session.Unlocked;
        CompleteAction();features.Begin(session);
        if(!session.Unlocked) {
            ImGui.TextUnformatted("Master portal");
            ImGui.TextWrapped("Enter your administrator password to manage pets. This page is password protected.");
            ImGui.BeginDisabled(session.Busy);
            ImGui.TextUnformatted("Administrator password");ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##AdministratorPassword",ref password,4096,ImGuiInputTextFlags.Password);EditingText|=ImGui.IsItemActive();
            ImGui.BeginDisabled(password.Length==0);
            if(ImGui.Button("Unlock master portal")){session.Unlock(password);password="";}
            ImGui.EndDisabled();ImGui.EndDisabled();
            ImGui.TextWrapped("The password is kept only while this window is open. Closing the window or locking clears it.");
            DrawFeedback();return;
        }
        ImGui.BeginDisabled(session.Busy);
        ImGui.PushStyleColor(ImGuiCol.Button,Style.Accent);ImGui.PushStyleColor(ImGuiCol.Text,new Vector4(.12f,.08f,.19f,1));
        if(ImGui.Button("Refresh now",new Vector2(190,44))) {
            if(session.Dashboard?.Selected is {Paired:true} pet)session.Action(new(){Action="requestReport",Pet=pet.Name});
            else session.Refresh();
        }
        ImGui.PopStyleColor(2);
        ImGui.EndDisabled();ImGui.SameLine();
        var auto=session.AutoRefresh;if(ImGui.Checkbox("Live updates while viewing",ref auto))session.AutoRefresh=auto;
        ImGui.SameLine();if(ImGui.Button("Lock portal")){Lock();return;}
        var dashboard=session.Dashboard!;
        ImGui.TextUnformatted($"Service {(dashboard.Service.DatabaseReady ? "connected" : "unavailable")} · Discord {(dashboard.Service.DiscordReady ? "connected" : "offline")}");
        DrawFeedback();
        ImGui.BeginDisabled(session.Busy);
        Input("Find a pet",ref filter,32);
        if(ImGui.BeginCombo("Pet profile",session.Selected.Length>0 ? session.Selected : "Choose a pet")) {
            foreach(var pet in dashboard.Profiles.Where(p=>p.Name.Contains(filter,StringComparison.OrdinalIgnoreCase)))
                if(ImGui.Selectable(pet.Name+" — "+MasterPortalPolicy.State(pet,session.Now),pet.Name==session.Selected)) {
                    pairingCode="";pairingPet="";reloadSettings=false;session.Select(pet.Name);
                }
            ImGui.EndCombo();
        }
        Input("New pet name",ref newPet,32);
        ImGui.BeginDisabled(!MasterPortalPolicy.ValidName(newPet));
        if(ImGui.Button("Add pet"))session.Action(new AdminAction{Action="createPet",Name=newPet});
        ImGui.EndDisabled();
        if(dashboard.Profiles.Count==0)ImGui.TextWrapped("Add a pet profile, then generate a pairing code to connect their plugin.");
        var detail=dashboard.Selected;
        var viewingOverview=false;var viewingChats=false;var viewingSettings=false;var settingsSection="controls";var settingsCharacter="";
        if(detail is not null && detail.Name==session.Selected) {
            ImGui.Separator();
            if(ImGui.Button("Generate pairing code")) {
                pairingCode="";session.Action(new AdminAction{Action="pair",Pet=detail.Name});
            }
            ImGui.SameLine();
            if(ImGui.Button("Revoke device…"))Confirm(new AdminAction{Action="revoke",Pet=detail.Name},
                "Disconnect this device and clear its unused pairing code? A new pairing is required to reconnect.");
            if(pairingCode.Length>0 && pairingPet==detail.Name) {
                ImGui.TextUnformatted("Pairing code");ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##MasterPairingCode",ref pairingCode,80,ImGuiInputTextFlags.ReadOnly);EditingText|=ImGui.IsItemActive();
                if(ImGui.Button("Copy pairing code"))ImGui.SetClipboardText(pairingCode);
                ImGui.SameLine();if(ImGui.Button("Clear code"))pairingCode="";
                ImGui.TextUnformatted("Expires "+LocalDate(pairingExpiry)+" · single use");
            }
            if(ImGui.BeginTabBar("masterdetail")) {
                if(ImGui.BeginTabItem("Overview")){viewingOverview=true;DrawStatus(detail);DrawOverview(detail);var dueTasks=detail.Features.Tasks.Where(t=>t.State is "pending" or "in-progress").ToList();ImGui.TextColored(Style.Accent,"Tasks awaiting attention: "+dueTasks.Count);foreach(var task in dueTasks.Take(5))ImGui.TextWrapped(task.Title+(task.DueAtUtc is not null?" · Due "+LocalDate(task.DueAtUtc):""));DrawAttention(detail);ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Inventory")){viewingSettings=true;settingsSection="inventory";features.Inventory(session,detail);settingsCharacter=features.InventoryCharacter;ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Tasks")){viewingSettings=true;settingsSection="tasks";features.Tasks(session,detail);ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Gameplay lock")){viewingSettings=true;features.Controls(session,detail);ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Integrations")){viewingSettings=true;features.Integrations(session,detail);settingsCharacter=features.IntegrationCharacter;ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Master identity")){viewingSettings=true;features.Identity(session);ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Rewards")){viewingSettings=true;settingsSection="rewards";features.Rewards(session,detail);ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Travel")){DrawTravel(detail);ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Chat messages")){viewingChats=true;DrawChat(detail);ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Activity")){DrawTimeline(detail,"");DrawActivity(detail);ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Playtime")){DrawPlaytime(detail);ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Duties")){DrawTimeline(detail,"duty");ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Loot & inventory")){ImGui.TextWrapped("Inventory changes are observed after container baseline. Source is unconfirmed unless independently identified.");DrawTimeline(detail,"inventory");ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Trades")){ImGui.TextWrapped("Trade contents and results from the pet's perspective. Gil amounts belong to the trade, not the character's balance.");DrawTimeline(detail,"trade");ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Retainers")){DrawTimeline(detail,"retainer");ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Reminders")){features.Reminders(session,detail);ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Messages")){ImGui.BeginDisabled(FeaturePortalView.Paused(detail));DrawMessage(detail);ImGui.EndDisabled();DrawPending(detail);ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Settings")){viewingSettings=true;DrawDynamic(detail);ImGui.EndTabItem();}
                if(ImGui.BeginTabItem("Profile")){DrawProfile(detail);ImGui.EndTabItem();}
                ImGui.EndTabBar();
            }
        }
        EditingText|=features.EditingText;ImGui.EndDisabled();
        DrawConfirmation();
        if(!ImGui.GetIO().AppFocusLost)session.TickVisible(viewingOverview,viewingChats,viewingSettings,settingsSection,settingsCharacter);else session.Hide();
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
        ImGui.InputInt("Expire after minutes (0 = no expiry)",ref draft.Expiry);
        ImGui.BeginDisabled(pet.Archived || string.IsNullOrWhiteSpace(draft.Text) || draft.Expiry is <0 or >10080);
        if(ImGui.Button("Send chat message"))session.Action(new(){Action="sendChat",Pet=pet.Name,Text=draft.Text.Trim(),ExpiresMinutes=draft.Expiry});
        ImGui.EndDisabled();ImGui.SameLine();if(ImGui.Button("Reload delivery status"))session.Refresh();
        ImGui.TextColored(Style.Muted,"[Master] "+draft.Text);
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
    private void DrawTravel(AdminPet pet)
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
        if(ImGui.BeginCombo("Travel method",ActivityLabels.TravelMethod(travelMethod))) {
            foreach(var method in new[]{"","teleport","return","aethernet","area-transfer","zone-transition"})if(ImGui.Selectable(ActivityLabels.TravelMethod(method),method==travelMethod)){travelMethod=method;historyPageKind="";}
            ImGui.EndCombo();
        }
        DrawTimeline(pet,"travel");
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
        if(pet.Settings.Templates.Count>0 && ImGui.BeginCombo("Saved template","Choose…")) {
            foreach(var template in pet.Settings.Templates)if(ImGui.Selectable(template.Name)){draft.Text=template.Text;draft.Choices=string.Join('\n',template.Choices);draft.AllowReply=template.AllowReply;}
            ImGui.EndCombo();
        }
        var error=MasterPortalPolicy.ChoiceError(draft.Choices,draft.AllowReply,out var labels);
        if(error is not null)ImGui.TextWrapped(error);
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(draft.Text) || error is not null);
        if(ImGui.Button("Send prompt"))session.Action(new AdminAction{Action="sendMessage",Pet=pet.Name,Text=draft.Text.Trim(),Choices=labels,AllowReply=draft.AllowReply});
        ImGui.EndDisabled();
        Input("Template name",ref templateName,60);
        ImGui.BeginDisabled(templateName.Trim().Length==0 || string.IsNullOrWhiteSpace(draft.Text) || error is not null || pet.Settings.Templates.Count>=20);
        if(ImGui.Button("Save template")) {
            var config=Clone(pet.Settings);config.Templates.RemoveAll(t=>t.Name==templateName.Trim());config.Templates.Add(new(){Name=templateName.Trim(),Text=draft.Text,Choices=labels,AllowReply=draft.AllowReply});
            session.Action(new(){Action="saveSettings",Pet=pet.Name,Settings=config});
        }
        ImGui.EndDisabled();
        if(ImGui.TreeNode("Preview prompt")){ImGui.TextColored(Style.Accent,"Message from Master");ImGui.TextWrapped(draft.Text);foreach(var label in labels)ImGui.Button(label+"##preview");ImGui.TreePop();}
    }
    private void DrawReminders(AdminPet pet)
    {
        if(!ImGui.CollapsingHeader("Daily reminders"))return;
        ImGui.TextWrapped("Enter daily times on your computer’s clock. The pet sees the equivalent time on theirs.");
        Input("Reminder label",ref reminderLabel,160);Input("Your daily time (HH:mm)",ref reminderTime,5);
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(reminderLabel) || !MasterPortalPolicy.ValidTime(reminderTime));
        if(ImGui.Button("Add daily reminder"))session.Action(new AdminAction{Action="addReminder",Pet=pet.Name,Label=reminderLabel.Trim(),Time=reminderTime,TimeZone=LocalClock.ZoneId});
        ImGui.EndDisabled();
        foreach(var reminder in pet.Reminders) {
            ImGui.PushID(reminder.Id);ImGui.Separator();
            ImGui.TextWrapped((reminder.Enabled ? "" : "Disabled · ")+reminder.Label);
            ImGui.TextUnformatted("Next daily time: "+LocalDate(reminder.NextDueAtUtc));
            if(reminder.Enabled && ImGui.Button("Disable…"))Confirm(new AdminAction{Action="disableReminder",Pet=pet.Name,ReminderId=reminder.Id},
                "Disable this reminder and cancel its pending occurrences? This does not record Taken.");
            ImGui.PopID();
        }
        if(pet.Reminders.Count==0)ImGui.TextUnformatted("No daily reminders yet.");
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
                "Remove this pending prompt without recording a reply or Taken?");
            ImGui.PopID();
        }
        if(pet.Pending.Count==0)ImGui.TextUnformatted("No pending prompts.");
    }
    private static void DrawActivity(AdminPet pet)
    {
        if(!ImGui.CollapsingHeader("Recent responses"))return;
        foreach(var entry in pet.Activity) {
            ImGui.Separator();
            ImGui.TextUnformatted(entry.Action switch {
                "complete"=>"Reported completion","decline"=>"Declined","response"=>"Sent a response","choice"=>"Selected a reply button","reply"=>"Sent a reply","taken"=>"Reported Taken","snooze"=>$"{(entry.SnoozeMode=="random-snooze"?"Randomly snoozed":"Snoozed")} for {entry.Minutes} minutes",
                "pause"=>"Activated kill switch","release"=>"Activated safe mode","attention"=>"Called the master","displayed"=>"Prompt displayed",_=>"Prompt choice"});
            if(entry.Label.Length>0)ImGui.TextWrapped(entry.Label);if(entry.OptionLabel is not null)ImGui.TextWrapped("Selected: "+entry.OptionLabel);
            if(entry.Reply is not null)ImGui.TextWrapped(entry.Reply);
            ImGui.TextUnformatted(LocalDate(entry.ReceivedAtUtc));
        }
        if(pet.Activity.Count==0)ImGui.TextUnformatted("No recent responses.");
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
        Style.Title("Attention requests","Contact requests awaiting your response");
        foreach(var item in pet.Attention.Where(a=>a.HandledAtUtc is null)) {
            ImGui.PushID(item.Id);ImGui.TextWrapped(item.Text);ImGui.TextColored(Style.Muted,LocalDate(item.CreatedAtUtc));
            if(ImGui.Button("Mark handled"))session.Action(new(){Action="handleAttention",Pet=pet.Name,EventId=item.Id});
            ImGui.SameLine();if(ImGui.Button("Prepare reply")){if(!drafts.ContainsKey(pet.Name))drafts[pet.Name]=new();drafts[pet.Name].Text="I'm here. What do you need?";}
            ImGui.PopID();
        }
        if(!pet.Attention.Any(a=>a.HandledAtUtc is null))ImGui.TextColored(Style.Muted,"No requests waiting.");
    }
    private void DrawPlaytime(AdminPet pet)
    {
        DrawOverview(pet);
        Style.Title("Daily playtime","Time recorded on each day");Style.DailyChart(pet.Stats.Daily);
        Input("From day (yyyy-MM-dd)",ref statFrom,10);Input("Through day (yyyy-MM-dd)",ref statTo,10);
        var custom=pet.Stats.Daily.Where(d=>(statFrom.Length==0 || string.CompareOrdinal(d.Day,statFrom)>=0) && (statTo.Length==0 || string.CompareOrdinal(d.Day,statTo)<=0)).Sum(d=>d.Seconds);
        ImGui.TextColored(Style.Accent,"Selected period · "+Duration(TimeSpan.FromSeconds(custom)));
        ImGui.TextWrapped($"This week {Duration(TimeSpan.FromSeconds(pet.Stats.WeekSeconds))} · This month {Duration(TimeSpan.FromSeconds(pet.Stats.MonthSeconds))}");
        ImGui.TextWrapped($"{pet.Stats.SessionCount} observed sessions · average {Duration(TimeSpan.FromSeconds(pet.Stats.AverageSessionSeconds))} · longest {Duration(TimeSpan.FromSeconds(pet.Stats.LongestSessionSeconds))}");
        ImGui.TextWrapped("Only recorded intervals count. Unobserved time and kill-switch pauses are excluded. Flags describe game state, not human engagement.");
        foreach(var (title,rows) in new[]{("By job",pet.Stats.Jobs),("By location",pet.Stats.Zones),("By character",pet.Stats.Characters)}) {
            if(ImGui.CollapsingHeader(title))foreach(var row in rows) {ImGui.TextWrapped(row.Label);ImGui.ProgressBar((float)(row.Seconds/Math.Max(1,pet.Stats.TotalSeconds)),new Vector2(-1,20),Duration(TimeSpan.FromSeconds(row.Seconds)));}
        }
    }
    private void DrawTimeline(AdminPet pet,string kind)
    {
        var queryKind=kind.Length>0?kind:"session,zone,state,job,duty,inventory,loot,trade,retainer,gap,travel";
        if(historyPageKind!=pet.Name+queryKind && !session.Busy){historyPageKind=pet.Name+queryKind;session.LoadHistory(queryKind,"","","",method:kind=="travel"?travelMethod:"");}
        Input("Find an observation",ref timelineFilter,100);
        Input("From local date (yyyy-MM-dd)",ref historyFrom,10);Input("Before local date (yyyy-MM-dd)",ref historyTo,10);
        if(ImGui.Button("Apply history filters")) {
            if(TryHistoryDate(historyFrom,out var from) && TryHistoryDate(historyTo,out var to))session.LoadHistory(queryKind,from,to,timelineFilter,method:kind=="travel"?travelMethod:"");
        }
        ImGui.SameLine();if(ImGui.Button("Export displayed CSV"))ExportHistory(session.History,pet.Name);
        var records=session.History;
        ImGui.TextColored(Style.Muted,$"{records.Count} observations loaded · older records remain in the service");
        foreach(var r in records) {
            ImGui.Separator();ImGui.TextColored(Style.Accent,ActivityLabels.Kind(r.Kind)+" · "+LocalDate(r.AtUtc));
            ImGui.TextColored(Style.Muted,r.CharacterName+" @ "+r.HomeWorld);
            if(r.Kind=="trade"){DrawTrade(r);continue;}
            foreach(var (key,value) in r.Data)ImGui.TextWrapped(ActivityLabels.Field(key)+": "+(r.Kind=="travel" && key=="method"?ActivityLabels.TravelMethod(ActivityLabels.Value(value)):key=="outcome" && ActivityLabels.Value(value)=="arrival-observed"?"Arrival observed":ActivityLabels.Value(value)));
        }
        if(session.HistoryCursor.Length>0 && ImGui.Button("Load older observations")){if(TryHistoryDate(historyFrom,out var from) && TryHistoryDate(historyTo,out var to))session.LoadHistory(queryKind,from,to,timelineFilter,true,method:kind=="travel"?travelMethod:"");}
        if(records.Count==0)ImGui.TextColored(Style.Muted,"No matching observations yet.");
    }
    private static void DrawTrade(ActivityRecord record)
    {
        var display=TradePresentation.Read(record,id=>Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>()?.GetRowOrDefault(id)?.Name.ToString()??$"Item {id}");
        ImGui.TextColored(Style.Accent,display.Outcome);
        ImGui.PushID(record.Id);
        if(ImGui.BeginTable("tradecontents",2,ImGuiTableFlags.SizingStretchSame|ImGuiTableFlags.BordersInnerV)) {
            ImGui.TableNextColumn();ImGui.TextUnformatted(display.Outcome=="Completed"?"Pet gave":"Pet offered to give");
            ImGui.TextWrapped(display.GiveItems);ImGui.TextUnformatted(display.GiveGil);
            ImGui.TableNextColumn();ImGui.TextUnformatted(display.Outcome=="Completed"?"Pet received":"Pet was offered");
            ImGui.TextWrapped(display.ReceiveItems);ImGui.TextUnformatted(display.ReceiveGil);ImGui.EndTable();
        }
        ImGui.PopID();ImGui.TextWrapped(display.Detail);
    }
    private static bool TryHistoryDate(string text,out string utc)
    {
        utc="";if(text.Length==0)return true;
        if(!DateTime.TryParseExact(text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))return false;
        return LocalClock.TryInput(date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+" 00:00",out utc!);
    }
    private void ExportHistory(List<ActivityRecord> records,string name)
    {
        try {
            static string Cell(string text)=>"\""+(text.Length>0 && "=+-@".Contains(text[0])?"\t":"")+text.Replace("\"","\"\"")+"\"";
            var rows=new List<string>{"Time local (UTC offset included),Character,Home world,Kind,Details"};
            rows.AddRange(records.Select(r=>string.Join(',',new[]{LocalClock.Export(r.AtUtc),r.CharacterName,r.HomeWorld,r.Kind,LocalClock.ExportDetails(r.Data)}.Select(Cell))));
            var folder=Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(),"exports");Directory.CreateDirectory(folder);
            var file=Path.Combine(folder,$"PetService-{name}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");File.WriteAllLines(file,rows,new System.Text.UTF8Encoding(true));
            Plugin.Chat.Print("Pet Service: CSV saved to "+file);
        } catch {Plugin.Chat.PrintError("Pet Service: Could not save the CSV export.");}
    }
    private void DrawDynamic(AdminPet pet)
    {
        var discard=reloadSettings && session.SettingsReadCount>reloadAfterRead;
        settingsEditor.Receive(pet.Name,pet.Settings,discard);
        if(discard)reloadSettings=false;
        var editSettings=settingsEditor.Value;
        Style.Title("Speech and contact settings","Garbling and contact presets for this pet");
        var garble=editSettings.GarbleEnabled;if(ImGui.Checkbox("Garble outgoing speech",ref garble))editSettings.GarbleEnabled=garble;
        var strength=editSettings.GarbleStrength;if(ImGui.SliderInt("Strength",ref strength,1,100))editSettings.GarbleStrength=strength;
        if(ImGui.BeginCombo("Style",editSettings.GarbleStyle)) {foreach(var name in new[]{"muffled","soft","playful"})if(ImGui.Selectable(name))editSettings.GarbleStyle=name;ImGui.EndCombo();}
        ImGui.TextWrapped("Transformations happen locally. Incoming chat, commands and Pet Service replies are untouched. The pet's kill switch disables garbling.");
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
        if(ImGui.Button(pet.Archived?"Restore profile…":"Archive profile…"))Confirm(new(){Action=pet.Archived?"restorePet":"archivePet",Pet=pet.Name},pet.Archived?"Restore this profile? Generate a new pairing code afterward.":"Archive and revoke access? History stays saved; pending prompts are cancelled.");
        if(ImGui.Button("Permanently delete profile…")){deleteName="";Confirm(new(){Action="deletePet",Pet=pet.Name},"Delete this pet, pairing, reminders, prompts and service history permanently? Already delivered Discord messages remain. Type the profile name to confirm.");}
    }
    private void Input(string label,ref string value,int max)
    {
        ImGui.TextUnformatted(label);ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##"+label,ref value,max);EditingText|=ImGui.IsItemActive();
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
        if(action.Action=="addReminder")reminderLabel="";
        if(action.Action=="pair"){pairingCode=result.Code ?? "";pairingPet=result.Pet;pairingExpiry=result.ExpiresAtUtc ?? "";}
        session.ClearCompletion();
    }
    private static string LocalDate(string? value)=>DateTimeOffset.TryParse(value,out var date) ? date.ToLocalTime().ToString("g",CultureInfo.CurrentCulture) : "Not observed";
    private static string Duration(TimeSpan span)=>$"{Math.Max(0,(int)span.TotalHours)}h {Math.Max(0,span.Minutes)}m";
    internal void Hide()=>session.Hide();
    internal void ClearTextFocus()=>EditingText=false;
    private void ClearFields()
    {
        password="";newPet="";filter="";reminderLabel="";reminderTime="18:00";pairingCode="";pairingPet="";pairingExpiry="";
        confirmation=null;confirmationText="";confirmRequested=false;drafts.Clear();chatDrafts.Clear();travelMethod="";EditingText=false;historyPageKind="";historyFrom="";historyTo="";statFrom="";statTo="";settingsEditor.Clear();reloadSettings=false;deleteName="";timelineFilter="";
    }
    internal void Lock(){features.Clear();session.Lock();ClearFields();hadAccess=false;}
    public void Dispose(){features.Dispose();ClearFields();session.Dispose();}
}
