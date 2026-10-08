using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
namespace PetService.Windows;
internal sealed class SettingsWindow : Window,IDisposable
{
    private readonly Plugin plugin;
    private string code="";
    private readonly PetFeatureView features;
    private bool selectCharacters;
    private bool selectContact;
    internal void OpenContact(){selectContact=true;IsOpen=true;}
    private readonly MasterPortalView master=new();
    internal bool EditingText {get;private set;}
    internal SettingsWindow(Plugin plugin):base("Pet Service")
    {
        this.plugin=plugin;features=new(plugin);SizeConstraints=new(){MinimumSize=new Vector2(650,490),MaximumSize=new(float.MaxValue)};
        Size=new Vector2(850,690);SizeCondition=ImGuiCond.FirstUseEver;
    }
    public override void PreDraw(){Style.ApplyTheme(plugin.SharingAllowed?plugin.Configuration.Features.Identity.Theme:"#baa0ff");Style.Push();}
    public override void PostDraw()=>Style.Pop();
    public override void Draw()
    {
        // Content padding must not change Dalamud's title-bar button geometry.
        Style.PushContent();
        try {DrawContent();} finally {Style.PopContent();}
    }
    private void DrawContent()
    {
        EditingText=false;
        Style.Title("PET SERVICE","Pet and Master controls · 0.4.0.1");
        ImGui.BeginChild("PetServiceMain",new Vector2(0,-85),false);
        var masterShown=false;
        if(ImGui.BeginTabBar("roles")) {
            if(ImGui.BeginTabItem("Pet",selectContact?ImGuiTabItemFlags.SetSelected:ImGuiTabItemFlags.None)){DrawPet();ImGui.EndTabItem();}
            if(ImGui.BeginTabItem("Master")){masterShown=true;master.Draw();EditingText|=master.EditingText;ImGui.EndTabItem();}
            ImGui.EndTabBar();
        }
        ImGui.EndChild();
        if(!masterShown)master.Hide();
        ImGui.Separator();var paused=plugin.KillSwitchOn;
        if(ImGui.Checkbox("Kill switch · pause this device",ref paused))plugin.SetEnabled(!paused);
        if(paused)ImGui.TextColored(Style.Accent,$"{plugin.MasterName} input, recording, sharing and garbling are paused.");
        if(plugin.SaveError.Length>0)ImGui.TextWrapped(plugin.SaveError);
        if(!paused && plugin.ActivityError.Length>0)ImGui.TextWrapped(plugin.ActivityError);
    }
    private void DrawPet()
    {
        var paused=plugin.IsPaired && Plugin.ClientState.IsLoggedIn && !plugin.CharacterAllowed;
        if(paused){Style.Title("Character access paused","This character is not enabled for PetService.");ImGui.TextWrapped("Sharing and "+plugin.MasterName+" controls are paused. To allow access, enable this character on Pet → Characters.");if(ImGui.Button("Open Characters",new Vector2(-1,42)))selectCharacters=true;ImGui.Spacing();}
        if(!ImGui.BeginTabBar("petpages"))return;
        if(!paused && ImGui.BeginTabItem("Home")) {
            features.Home();if(plugin.LockRequested)Style.Metric("Gameplay",plugin.Configuration.Features.Lock?.State??"Queued","petlock");ImGui.Spacing();Style.Title(plugin.IsPaired?plugin.Configuration.PetName:"Welcome home",plugin.ServiceStatus);
            Style.Metric("Your connection",plugin.KillSwitchOn?"Paused":plugin.IsPaired?"Paired":"Setup needed","petconnection");
            ImGui.Spacing();
            Style.Metric("Your voice",plugin.KillSwitchOn || !plugin.Configuration.Dynamic.GarbleEnabled?"Natural":"Garbling · "+plugin.Configuration.Dynamic.GarbleStyle,"petvoice");
            ImGui.TextWrapped(plugin.KillSwitchOn?"You choose when to resume. "+plugin.MasterName+" cannot turn your kill switch off.":"Use Contact to send a little signal home. Use Setup to manage your pairing.");
            ImGui.EndTabItem();
        }
        if(!paused && ImGui.BeginTabItem("Contact",selectContact?ImGuiTabItemFlags.SetSelected:ImGuiTabItemFlags.None)) {
            selectContact=false;
            Style.Title("A little signal home","Choose what you want your "+plugin.MasterName+" to hear.");
            ImGui.BeginDisabled(!plugin.CanCallMaster);
            var icons=new[]{FontAwesomeIcon.Bell,FontAwesomeIcon.LifeRing,FontAwesomeIcon.Cloud,FontAwesomeIcon.Heart,FontAwesomeIcon.Star,FontAwesomeIcon.Moon,FontAwesomeIcon.HandHoldingHeart,FontAwesomeIcon.Comment};
            for(var i=0;i<plugin.Configuration.Dynamic.Contacts.Count;i++) {
                var request=plugin.Configuration.Dynamic.Contacts[i];ImGui.PushID(i);
                if(Style.IconButton(icons[i%icons.Length],request,new Vector2(-1,52)))plugin.RequestAttention(request);
                ImGui.PopID();
            }
            ImGui.EndDisabled();
            ImGui.TextWrapped(plugin.CallCooldownSeconds>0?$"Contact queued. Next request in {plugin.CallCooldownSeconds}s.":"Requests are saved locally, then delivered through Discord when connected.");
            ImGui.EndTabItem();
        }
        if(ImGui.BeginTabItem("Characters",selectCharacters?ImGuiTabItemFlags.SetSelected:0)){selectCharacters=false;features.Characters();ImGui.EndTabItem();}
        if(!paused && ImGui.BeginTabItem("Tasks")){features.Tasks();EditingText|=features.EditingText;ImGui.EndTabItem();}
        if(ImGui.BeginTabItem("Setup")) {
            ImGui.TextWrapped(plugin.ServiceStatus);ImGui.Separator();
            if(!plugin.IsPaired){ImGui.InputText("Pairing code",ref code,80,ImGuiInputTextFlags.Password);EditingText|=ImGui.IsItemActive();ImGui.BeginDisabled(plugin.NetworkBusy || code.Length==0);if(ImGui.Button("Pair this device")){plugin.Pair(code);code="";}ImGui.EndDisabled();}
            else {ImGui.TextUnformatted("Paired as "+plugin.Configuration.PetName);if(ImGui.Button("Unpair…"))ImGui.OpenPopup("Unpair this device?");}
            var open=true;if(ImGui.BeginPopupModal("Unpair this device?",ref open,ImGuiWindowFlags.AlwaysAutoResize)) {
                ImGui.TextWrapped($"Clear pairing, cached prompts and unsent observations? Server history remains with {plugin.MasterName}.");
                if(ImGui.Button("Unpair")){plugin.Unpair();ImGui.CloseCurrentPopup();}ImGui.SameLine();if(ImGui.Button("Keep pairing"))ImGui.CloseCurrentPopup();ImGui.EndPopup();
            }
            ImGui.Spacing();
            DrawChatPreferences();
            if(ImGui.Button("Verify connection"))plugin.VerifyConnection();
            if(plugin.Configuration.Recovery.Count>0 || plugin.ArchivedRecoveryCount>0){ImGui.TextWrapped($"{plugin.Configuration.Recovery.Count} saved records need recovery; {plugin.ArchivedRecoveryCount} are archived locally. Fresh reports continue independently.");if(ImGui.Button("Retry saved records"))plugin.RetryRecovery();}
            if(ImGui.Button("What's new"))Plugin.Commands.ProcessCommand("/toh changes");ImGui.SameLine();if(ImGui.Button("Check for updates"))Plugin.Commands.ProcessCommand("/toh checkupdates");
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }
    private void DrawChatPreferences()
    {
        ImGui.Separator();ImGui.TextUnformatted(plugin.MasterName+" chat messages");
        ImGui.TextWrapped("Choose the message category; your FFXIV chat filters control which tabs display it.");
        if(ImGui.BeginCombo("Message output",plugin.Configuration.MasterChatType=="system"?"System messages":"Echo")) {
            foreach(var kind in new[]{"echo","system"})if(ImGui.Selectable(kind=="echo"?"Echo":"System messages",plugin.Configuration.MasterChatType==kind))plugin.Mutate(c=>c.MasterChatType=kind);
            ImGui.EndCombo();
        }
        var colours=new (string Label,ushort Id)[]{("Default",0),("Gold",540),("Green",504),("Blue",37),("Pink",561)};
        if(ImGui.BeginCombo("Message colour",colours.FirstOrDefault(c=>c.Id==plugin.Configuration.MasterChatColour).Label??"Default")) {
            foreach(var colour in colours)if(ImGui.Selectable(colour.Label,colour.Id==plugin.Configuration.MasterChatColour))plugin.Mutate(c=>c.MasterChatColour=colour.Id);
            ImGui.EndCombo();
        }
        var sound=plugin.Configuration.MasterChatSound;if(ImGui.Checkbox("Play a notification sound",ref sound))plugin.Mutate(c=>c.MasterChatSound=sound);
        if(ImGui.Button("Preview in chat"))MasterChatDelivery.Print(plugin.Configuration,"A little message from your "+plugin.MasterName+".");
        ImGui.Spacing();
    }
    internal void ClearTextFocus(){EditingText=false;master.ClearTextFocus();}
    public override void OnClose(){code="";EditingText=false;master.Lock();}
    public void Dispose(){features.Dispose();master.Dispose();}
}
