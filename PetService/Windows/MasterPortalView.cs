using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using PetService.Core;

namespace PetService.Windows;

internal sealed class MasterPortalView : IDisposable
{
    private readonly MasterPortalSession session=new(new MasterPortalClient());
    private string password="",newPet="",filter="",reminderLabel="",reminderTime="18:00";
    private string pairingCode="",pairingPet="",pairingExpiry="",confirmationText="";
    private bool hadAccess,confirmRequested;
    private AdminAction? confirmation;
    private sealed class Draft {internal string Text="",Choices="";internal bool AllowReply=true;}
    private readonly Dictionary<string,Draft> drafts=[];
    internal bool EditingText { get; private set; }

    internal void Draw()
    {
        EditingText=false;session.Update();
        if(hadAccess && !session.Unlocked)ClearFields();
        hadAccess=session.Unlocked;
        CompleteAction();
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
        if(ImGui.Button("Refresh"))session.Refresh();
        ImGui.EndDisabled();ImGui.SameLine();
        var auto=session.AutoRefresh;if(ImGui.Checkbox("Auto-refresh",ref auto))session.AutoRefresh=auto;
        ImGui.SameLine();if(ImGui.Button("Lock portal")){Lock();return;}
        var dashboard=session.Dashboard!;
        ImGui.TextUnformatted($"Service {(dashboard.Service.DatabaseReady ? "connected" : "unavailable")} · Discord {(dashboard.Service.DiscordReady ? "connected" : "offline")}");
        DrawFeedback();
        ImGui.BeginDisabled(session.Busy);
        Input("Find a pet",ref filter,32);
        if(ImGui.BeginCombo("Pet profile",session.Selected.Length>0 ? session.Selected : "Choose a pet")) {
            foreach(var pet in dashboard.Profiles.Where(p=>p.Name.Contains(filter,StringComparison.OrdinalIgnoreCase)))
                if(ImGui.Selectable(pet.Name+" — "+MasterPortalPolicy.State(pet,session.Now),pet.Name==session.Selected)) {
                    pairingCode="";pairingPet="";session.Select(pet.Name);
                }
            ImGui.EndCombo();
        }
        Input("New pet name",ref newPet,32);
        ImGui.BeginDisabled(!MasterPortalPolicy.ValidName(newPet));
        if(ImGui.Button("Add pet"))session.Action(new AdminAction{Action="createPet",Name=newPet});
        ImGui.EndDisabled();
        if(dashboard.Profiles.Count==0)ImGui.TextWrapped("Add a pet profile, then generate a pairing code to connect their plugin.");
        var detail=dashboard.Selected;
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
            DrawStatus(detail);DrawMessage(detail);DrawReminders(detail);DrawPending(detail);DrawActivity(detail);
        }
        ImGui.EndDisabled();
        DrawConfirmation();
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
        if(!ImGui.CollapsingHeader("Status and location",ImGuiTreeNodeFlags.DefaultOpen))return;
        var status=pet.Status.Reported;var fresh=MasterPortalPolicy.Fresh(pet,session.Now);
        ImGui.TextUnformatted(MasterPortalPolicy.State(pet,session.Now));
        ImGui.TextUnformatted("Last report: "+LocalDate(pet.Status.LastSeenAtUtc));
        if(status is null){ImGui.TextUnformatted("Awaiting a character observation.");return;}
        ImGui.TextWrapped("Character: "+status.CharacterName+" · home world "+status.HomeWorld);
        ImGui.TextWrapped("Last observed location: "+(status.Zone.Length>0 ? status.Zone : "Not observed"));
        ImGui.TextWrapped(status.CurrentWorld+" · "+status.DataCenter);
        ImGui.TextUnformatted((status.LoginTimeSource=="login-event" ? "Login observed: " : "First seen: ")+LocalDate(status.LoginObservedAtUtc));
        var duration=fresh && status.LoggedIn && DateTimeOffset.TryParse(status.LoginObservedAtUtc,out var login)
            ? Duration(session.Now-login) : "Unknown";
        ImGui.TextUnformatted("Observed session: "+duration);
        if(!fresh){ImGui.TextWrapped("Current state is unknown; the location above is the last observation.");return;}
        if(!status.LoggedIn)return;
        var flags=new List<string>();
        if(!status.Ready)flags.Add("Loading / not ready");if(status.InDuty)flags.Add("In duty");if(status.InCombat)flags.Add("In combat");
        if(status.IsAfk)flags.Add("AFK");if(status.GameIdle)flags.Add("Game idle");if(status.InputGuardActive)flags.Add("Prompt guard active");if(status.LocalRelease)flags.Add("Local release");
        if(flags.Count>0)ImGui.TextWrapped(string.Join(" · ",flags));
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
        var error=MasterPortalPolicy.ChoiceError(draft.Choices,draft.AllowReply,out var labels);
        if(error is not null)ImGui.TextWrapped(error);
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(draft.Text) || error is not null);
        if(ImGui.Button("Send prompt"))session.Action(new AdminAction{Action="sendMessage",Pet=pet.Name,Text=draft.Text.Trim(),Choices=labels,AllowReply=draft.AllowReply});
        ImGui.EndDisabled();
    }
    private void DrawReminders(AdminPet pet)
    {
        if(!ImGui.CollapsingHeader("Daily reminders"))return;
        ImGui.TextWrapped("New schedules use this pet's clock. Due times below use your computer's clock.");
        Input("Reminder label",ref reminderLabel,160);Input("Pet's daily time (HH:mm)",ref reminderTime,5);
        var clockKnown=!string.IsNullOrEmpty(pet.Status.Reported?.TimeZone);
        if(!clockKnown)ImGui.TextWrapped("Connect this pet's plugin once before adding a reminder.");
        ImGui.BeginDisabled(!clockKnown || string.IsNullOrWhiteSpace(reminderLabel) || !MasterPortalPolicy.ValidTime(reminderTime));
        if(ImGui.Button("Add daily reminder"))session.Action(new AdminAction{Action="addReminder",Pet=pet.Name,Label=reminderLabel.Trim(),Time=reminderTime});
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
                "choice"=>"Selected a reply button","reply"=>"Sent a reply","taken"=>"Reported Taken","snooze"=>$"Snoozed for {entry.Minutes} minutes",
                "pause"=>"Activated kill switch","release"=>"Activated safe mode","attention"=>"Called the master","displayed"=>"Prompt displayed",_=>"Prompt choice"});
            if(entry.Label.Length>0)ImGui.TextWrapped(entry.Label);
            if(entry.Reply is not null)ImGui.TextWrapped(entry.Reply);
            ImGui.TextUnformatted(LocalDate(entry.ReceivedAtUtc));
        }
        if(pet.Activity.Count==0)ImGui.TextUnformatted("No recent responses.");
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
            ImGui.BeginDisabled(session.Busy || confirmation is null);
            if(ImGui.Button("Confirm")){session.Action(confirmation!);confirmation=null;ImGui.CloseCurrentPopup();}
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
        if(action.Action=="createPet")newPet="";
        if(action.Action=="sendMessage" && action.Pet is not null)drafts.Remove(action.Pet);
        if(action.Action=="addReminder")reminderLabel="";
        if(action.Action=="pair"){pairingCode=result.Code ?? "";pairingPet=result.Pet;pairingExpiry=result.ExpiresAtUtc ?? "";}
        session.ClearCompletion();
    }
    private static string LocalDate(string? value)=>DateTimeOffset.TryParse(value,out var date) ? date.ToLocalTime().ToString("g",CultureInfo.CurrentCulture) : "Not observed";
    private static string Duration(TimeSpan span)=>$"{Math.Max(0,(int)span.TotalHours)}h {Math.Max(0,span.Minutes)}m";
    internal void ClearTextFocus()=>EditingText=false;
    private void ClearFields()
    {
        password="";newPet="";filter="";reminderLabel="";reminderTime="18:00";pairingCode="";pairingPet="";pairingExpiry="";
        confirmation=null;confirmationText="";confirmRequested=false;drafts.Clear();EditingText=false;
    }
    internal void Lock(){session.Lock();ClearFields();hadAccess=false;}
    public void Dispose(){ClearFields();session.Dispose();}
}
