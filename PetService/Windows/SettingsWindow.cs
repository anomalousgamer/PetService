using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
namespace PetService.Windows;
internal sealed class SettingsWindow : Window,IDisposable
{
    private readonly Plugin plugin;
    private string code="";
    private readonly MasterPortalView master=new();
    internal bool EditingText {get;private set;}
    internal SettingsWindow(Plugin plugin):base("Pet Service")
    {
        this.plugin=plugin;SizeConstraints=new(){MinimumSize=new Vector2(650,490),MaximumSize=new(float.MaxValue)};
        Size=new Vector2(850,690);SizeCondition=ImGuiCond.FirstUseEver;
    }
    public override void PreDraw()=>Style.Push();
    public override void PostDraw()=>Style.Pop();
    public override void Draw()
    {
        EditingText=false;
        Style.Title("PET SERVICE","A place for your shared dynamic · 0.2.0.0");
        ImGui.BeginChild("PetServiceMain",new Vector2(0,-85),false);
        if(ImGui.BeginTabBar("roles")) {
            if(ImGui.BeginTabItem("Pet")){DrawPet();ImGui.EndTabItem();}
            if(ImGui.BeginTabItem("Master")){master.Draw();EditingText|=master.EditingText;ImGui.EndTabItem();}
            ImGui.EndTabBar();
        }
        ImGui.EndChild();
        ImGui.Separator();var paused=plugin.KillSwitchOn;
        if(ImGui.Checkbox("Kill switch · pause this device",ref paused))plugin.SetEnabled(!paused);
        if(paused)ImGui.TextColored(Style.Accent,"Master input, recording, sharing and garbling are paused.");
        if(plugin.SaveError.Length>0)ImGui.TextWrapped(plugin.SaveError);
        if(!paused && plugin.ActivityError.Length>0)ImGui.TextWrapped(plugin.ActivityError);
    }
    private void DrawPet()
    {
        if(!ImGui.BeginTabBar("petpages"))return;
        if(ImGui.BeginTabItem("Home")) {
            ImGui.Spacing();Style.Title(plugin.IsPaired?plugin.Configuration.PetName:"Welcome home",plugin.ServiceStatus);
            Style.Metric("Your connection",plugin.KillSwitchOn?"Paused":plugin.IsPaired?"Paired":"Setup needed","petconnection");
            ImGui.Spacing();
            Style.Metric("Your voice",plugin.KillSwitchOn || !plugin.Configuration.Dynamic.GarbleEnabled?"Natural":"Garbling · "+plugin.Configuration.Dynamic.GarbleStyle,"petvoice");
            ImGui.TextWrapped(plugin.KillSwitchOn?"You choose when to resume. The master cannot turn your kill switch off.":"Use Contact to send a little signal home. Use Setup to see your pairing and sharing details.");
            if(Style.IconButton(FontAwesomeIcon.Heart,"Call master",new Vector2(-1,52)) && plugin.CanCallMaster)plugin.RequestAttention();
            if(plugin.CallCooldownSeconds>0)ImGui.TextColored(Style.Muted,$"Next contact in {plugin.CallCooldownSeconds}s.");
            ImGui.EndTabItem();
        }
        if(ImGui.BeginTabItem("Contact")) {
            Style.Title("A little signal home","Choose what you want your master to hear.");
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
        if(ImGui.BeginTabItem("Setup")) {
            ImGui.TextWrapped(plugin.ServiceStatus);ImGui.Separator();
            if(!plugin.IsPaired){ImGui.InputText("Pairing code",ref code,80,ImGuiInputTextFlags.Password);EditingText|=ImGui.IsItemActive();ImGui.BeginDisabled(plugin.NetworkBusy || code.Length==0);if(ImGui.Button("Pair this device")){plugin.Pair(code);code="";}ImGui.EndDisabled();}
            else {ImGui.TextUnformatted("Paired as "+plugin.Configuration.PetName);if(ImGui.Button("Unpair…"))ImGui.OpenPopup("Unpair this device?");}
            var open=true;if(ImGui.BeginPopupModal("Unpair this device?",ref open,ImGuiWindowFlags.AlwaysAutoResize)) {
                ImGui.TextWrapped("Clear pairing, cached prompts and unsent observations? Server history remains with the master.");
                if(ImGui.Button("Unpair")){plugin.Unpair();ImGui.CloseCurrentPopup();}ImGui.SameLine();if(ImGui.Button("Keep pairing"))ImGui.CloseCurrentPopup();ImGui.EndPopup();
            }
            ImGui.Spacing();Style.Title("What is shared","Recording begins only while paired and resumed.");
            ImGui.TextWrapped("The master can see observed playtime, your character, zone/world/DC and coordinates, duties, job/level and activity flags, supported inventory changes, trade offers and venture result screens. Explicit Pet Service requests, replies, choices, snoozes and safety notices are saved too.");
            ImGui.TextWrapped("Game chat, original or garbled, and conversation partners are never collected. Garbling works locally on outgoing speech. Trade completion and inventory item sources can be unknown; unobserved time is excluded.");
            ImGui.TextWrapped("The kill switch stops master prompts, recording, uploads and garbling. Only the safety notice is sent. /toh release frees gameplay for this login while sharing continues.");
            if(ImGui.Button("What's new"))Plugin.Commands.ProcessCommand("/toh changes");ImGui.SameLine();if(ImGui.Button("Check for updates"))Plugin.Commands.ProcessCommand("/toh checkupdates");
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }
    internal void ClearTextFocus(){EditingText=false;master.ClearTextFocus();}
    public override void OnClose(){code="";EditingText=false;master.Lock();}
    public void Dispose()=>master.Dispose();
}
