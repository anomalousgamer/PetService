using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.ImGuiFileDialog;
using PetService.Core;
namespace PetService.Windows;

internal sealed class PrivateChatWindow:Window,IDisposable
{
 private readonly Plugin plugin;internal readonly PrivateChatSession Session;
 private readonly ChatTranscript transcript=new();private readonly FileDialogManager files=new();
 private string password="",pet="",backupPassword="",search="",dialogName="",dialogError="";
 private bool master,settings,sized,openDialog,verifiedFingerprint,focusComposer;private string settingsTab="General";
 private Vector2 lastSize;private DialogTarget? dialog;
 private sealed record DialogTarget(string Kind,string Id,int Generation,string Name,string Scope,bool Master,ChatDevice? Device=null);
 internal bool EditingText{get;private set;}
 internal PrivateChatWindow(Plugin plugin):base("Private RP chat###PetServicePrivateChat")
 {this.plugin=plugin;Session=new(plugin);SizeConstraints=new(){MinimumSize=new(700,480),MaximumSize=new(float.MaxValue)};}
 internal void OpenPet(){master=false;settings=false;dialog=null;Session.SetAccount(false,plugin.Configuration.PetName,"");IsOpen=true;}
 internal void OpenMaster(string name,string secret){master=true;pet=name;password="";settings=false;dialog=null;Session.SetAccount(true,name,secret);IsOpen=true;}
 internal void OpenDefault(){if(master){settings=false;IsOpen=true;}else OpenPet();}
 public override void PreDraw(){if(!sized){Size=new(Math.Max(700,plugin.Configuration.ChatWindow.Width),Math.Max(480,plugin.Configuration.ChatWindow.Height));SizeCondition=ImGuiCond.FirstUseEver;sized=true;}Style.ApplyTheme(plugin.Configuration.Features.Identity.Theme);Style.Push();}
 public override void PostDraw()=>Style.Pop();
 public override void Draw()
 {
  plugin.DrawingPrivateChat=true;
  try{DrawContents();DrawDialog();}catch(Exception e){Session.ReportError("drawing private chat",e);ImGui.TextWrapped(Session.Error);}finally{plugin.DrawingPrivateChat=false;}
 }
 private void DrawContents()
 {
  lastSize=ImGui.GetWindowSize();EditingText=ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);Session.Tick(!ImGui.GetIO().AppFocusLost);files.Draw();
  if(master&&Session.Vault is null&&!Session.HasCredential){Style.Title("Master RP chat","Unlock this chat session.");ImGui.InputText("Pet profile",ref pet,32);ImGui.InputText("Master password",ref password,4096,ImGuiInputTextFlags.Password);if(ImGui.Button("Unlock chat")&&pet.Length>0&&password.Length>0){Session.SetAccount(true,pet,password);password="";}return;}
  Style.Title("PRIVATE RP CHAT",master?pet:plugin.Configuration.PetName);
  if(ImGui.Button(settings?"Back to conversations":"Chat settings"))settings=!settings;
  if(master){ImGui.SameLine();if(ImGui.Button("Lock chat")){Session.Lock();dialog=null;return;}}
  ImGui.SameLine();ImGui.TextColored(Style.Muted,Session.Paused?"Chat paused for character access":!Session.Ready?"Connecting…":Session.Preferences.GlobalPaused?"My chat is paused":"Connected · encrypted");
  ImGui.Separator();
  if(Session.Paused){ImGui.TextWrapped(plugin.IsPaired?"Private chat is paused on this unapproved character. Return to an approved character to resume.":"Pair this device on Pet → Setup to use private chat.");return;}
  if(Session.Vault is null){ImGui.TextWrapped(Session.Error.Length>0?Session.Error:"Opening protected chat storage…");return;}
  if(settings)DrawSettings();else DrawChat();
  ImGui.Separator();
  if(Session.Error.Length>0)ImGui.TextWrapped(Session.Error);
  else ImGui.TextWrapped(Session.PreferencesSaving?"Saving chat preferences…":Session.Notice.Length>0?Session.Notice:"Enter sends · Shift+Enter adds a line");
 }
 private string Label(ChatThread thread,int index){var title=Session.Title(thread);return title=="Conversation awaiting access"?"Conversation "+(index+1)+" · waiting for access":title;}
 private void DrawChat()
 {
  if(!ImGui.BeginTable("RP layout",2,ImGuiTableFlags.Resizable|ImGuiTableFlags.SizingStretchProp))return;
  try{
   ImGui.TableSetupColumn("Conversations",ImGuiTableColumnFlags.WidthFixed,230*ImGuiHelpers.GlobalScale);ImGui.TableSetupColumn("Conversation",ImGuiTableColumnFlags.WidthStretch);ImGui.TableNextColumn();
   ImGui.BeginDisabled(!Session.Ready);try{if(ImGui.Button("+ New conversation",new(-1,36))){Session.TitleDraft="New conversation";Session.Create();focusComposer=true;}}finally{ImGui.EndDisabled();}
   ImGui.SetNextItemWidth(-1);ImGui.InputTextWithHint("##FindConversation","Find a conversation",ref search,120);EditingText|=ImGui.IsItemActive();ImGui.Spacing();
   var conversations=Session.State.Threads.Where(t=>!t.Deleted).ToList();
   if(conversations.Count==0)ImGui.TextWrapped("Your conversations will appear here. Start one with New conversation.");
   for(var i=0;i<conversations.Count;i++){
    var thread=conversations[i];var label=Label(thread,i);if(search.Length>0&&!label.Contains(search,StringComparison.OrdinalIgnoreCase))continue;
    ImGui.PushID(thread.Id);try{
     var suffix=Session.DeletionPending(thread.Id)?" · deleting…":Session.Preferences.Paused.Contains(thread.Id)?" · paused":Session.Preferences.Muted.Contains(thread.Id)?" · muted":"";
     if(ImGui.Selectable(label+suffix,Session.Room==thread.Id))Session.Choose(thread.Id);
     ConversationMenu(thread,label);
    }finally{ImGui.PopID();}
   }
   ImGui.TableNextColumn();DrawConversation();
  }finally{ImGui.EndTable();}
 }
 private void ConversationMenu(ChatThread thread,string label)
 {
  if(!ImGui.BeginPopupContextItem("Conversation actions"))return;
  try{
   if(ImGui.MenuItem("Rename conversation…"))OpenAction("rename",thread,label);
   if(ImGui.MenuItem(Session.Preferences.Muted.Contains(thread.Id)?"Unmute notifications":"Mute notifications"))Session.Preference(thread.Id,muted:!Session.Preferences.Muted.Contains(thread.Id));
   if(ImGui.MenuItem(Session.Preferences.Paused.Contains(thread.Id)?"Resume this conversation":"Pause this conversation"))Session.Preference(thread.Id,paused:!Session.Preferences.Paused.Contains(thread.Id));
   ImGui.Separator();
   if(ImGui.MenuItem("Delete for both… (Ctrl+Shift)")){if(ImGui.GetIO().KeyCtrl&&ImGui.GetIO().KeyShift)OpenAction("delete",thread,label);else Session.Notice="Hold Ctrl+Shift while selecting Delete, then confirm in the chat dialog.";}
  }finally{ImGui.EndPopup();}
 }
 private void DrawConversation()
 {
  var thread=Session.State.Threads.FirstOrDefault(t=>t.Id==Session.Room&&!t.Deleted);
  if(thread is null){Style.Title(Session.Room.Length>0?"Creating your conversation…":"Choose a conversation","Use the list on the left, or start a new conversation.");return;}
  var title=Session.Title(thread);ImGui.TextWrapped(title);ImGui.Separator();
  if(Session.ConversationIssue(thread) is {Length:>0} issue){Style.Title("Conversation access needed",issue);ImGui.TextWrapped("Both participants verify their chat devices once. The device that already has this conversation then shares access automatically. If every original device is lost, restore an encrypted backup.");if(ImGui.Button("Set up conversation access")){settings=true;settingsTab="Devices";}return;}
  if(Session.ConversationPaused(thread.Id)){ImGui.TextColored(Style.Muted,Session.Preferences.GlobalPaused?"My RP chat is paused. Existing history remains readable.":"This conversation is paused. Existing history remains readable.");}
  var available=ImGui.GetContentRegionAvail().Y;var height=Math.Clamp(plugin.Configuration.ChatWindow.Composer,60,Math.Max(60,available*.55f));
  var visible=ImGui.BeginChild("RP transcript",new(0,Math.Max(70,available-height-110)),true);
  try{if(visible){if(Session.More){ImGui.BeginDisabled(Session.Busy);try{if(ImGui.SmallButton("Load older messages"))Session.Poll(true);}finally{ImGui.EndDisabled();}}var bottom=ImGui.GetScrollY()>=ImGui.GetScrollMaxY()-8;transcript.Draw(Session.Messages);if(bottom)ImGui.SetScrollHereY(1);}}finally{ImGui.EndChild();}
  ImGui.InvisibleButton("Resize message box",new(-1,8));if(ImGui.IsItemHovered()||ImGui.IsItemActive())ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);if(ImGui.IsItemActive())plugin.Configuration.ChatWindow.Composer=Math.Clamp(height-ImGui.GetIO().MouseDelta.Y,60,Math.Max(60,available*.55f));
  var vault=Session.Vault!;ImGui.SetNextItemWidth(220*ImGuiHelpers.GlobalScale);
  if(ImGui.BeginCombo("Speaking as",vault.Personas.FirstOrDefault(p=>p.Id==vault.Active)?.Name??vault.Personas.FirstOrDefault()?.Name??"Choose name")){try{foreach(var persona in vault.Personas)if(ImGui.Selectable(persona.Name,persona.Id==vault.Active)){vault.Active=persona.Id;Session.Save();}}finally{ImGui.EndCombo();}}
  ImGui.BeginDisabled(Session.Paused||Session.ConversationPaused(Session.Room)||Session.DeletionPending(Session.Room));
  try{
   var text=Session.Draft;var io=ImGui.GetIO();var ctrl=io.KeyCtrl;bool enter;
   try{if(focusComposer){ImGui.SetKeyboardFocusHere();focusComposer=false;}if(io.KeyShift&&ImGui.IsKeyDown(ImGuiKey.Enter))io.KeyCtrl=true;enter=ImGui.InputTextMultiline("##RP composer",ref text,10000,new(-1,height),ImGuiInputTextFlags.EnterReturnsTrue|ImGuiInputTextFlags.CtrlEnterForNewLine);}finally{io.KeyCtrl=ctrl;}
   EditingText|=ImGui.IsItemActive();if(text!=Session.Draft){Session.Draft=text;Session.StoreDraft();}if(enter)Session.Send();if(ImGui.Button("Send message"))Session.Send();ImGui.SameLine();ImGui.TextColored(Style.Muted,"Enter sends · Shift+Enter adds a line");
  }finally{ImGui.EndDisabled();}
 }
 private void DrawSettings()
 {
  if(!ImGui.BeginTabBar("Private chat settings"))return;
  try{
   foreach(var page in new[]{"General","Names","Devices","Backup and history"}){
    var flags=settingsTab==page?ImGuiTabItemFlags.SetSelected:ImGuiTabItemFlags.None;
    if(!ImGui.BeginTabItem(page,flags))continue;
    settingsTab="";try{switch(page){case "General":GeneralSettings();break;case "Names":NameSettings();break;case "Devices":DeviceSettings();break;default:BackupSettings();break;}}finally{ImGui.EndTabItem();}
   }
  }finally{ImGui.EndTabBar();}
 }
 private void GeneralSettings()
 {
  Style.Title("My chat connection","These controls affect your side of private RP chat.");var pause=Session.Preferences.GlobalPaused;
  ImGui.BeginDisabled(!Session.Ready);try{if(ImGui.Checkbox("Pause my RP chat",ref pause))Session.Preference(global:pause);}finally{ImGui.EndDisabled();}
  ImGui.TextWrapped("Pausing holds new sending and receiving in all of your conversations. The other participant can keep writing; their messages arrive when you resume.");
  ImGui.Spacing();Style.Title("Conversation controls","Right-click a conversation to rename, mute, pause or delete it.");
  ImGui.TextWrapped("Mute notifications keeps messages arriving without new-message alerts. Pause conversation holds sending and receiving for that conversation. These preferences synchronize across your devices.");
  ImGui.Spacing();ImGui.TextWrapped("The gameplay kill switch and Sleep do not pause RP chat. An unapproved character pauses the pet's chat connection.");
 }
 private void NameSettings()
 {
  var vault=Session.Vault!;Style.Title("Speaking names","Save names and colors here. Choose one above the message box while writing. Earlier messages keep their original name and color.");
  foreach(var persona in vault.Personas.ToList()){
   ImGui.PushID(persona.Id);try{var name=persona.Name;ImGui.SetNextItemWidth(250*ImGuiHelpers.GlobalScale);if(ImGui.InputText("Name",ref name,60)){persona.Name=name;Session.Save();}EditingText|=ImGui.IsItemActive();var color=persona.Color;if(Style.ColorField("Name color",ref color)){persona.Color=color;Session.Save();}ImGui.BeginDisabled(vault.Personas.Count<=1);try{if(ImGui.Button("Remove name")){vault.Personas.Remove(persona);Session.Save();}}finally{ImGui.EndDisabled();}ImGui.Separator();}finally{ImGui.PopID();}
  }
  if(vault.Personas.Count<40&&ImGui.Button("+ Add speaking name")){vault.Personas.Add(new());Session.Save();}
 }
 private void DeviceSettings()
 {
  var vault=Session.Vault!;Style.Title("Conversation access","Both participants open this page. Compare device fingerprints using your existing trusted contact, then verify the matching device. Existing conversation keys are shared automatically between verified devices.");
  if(Session.State.Devices.Count==0)ImGui.TextWrapped("Waiting for chat devices to connect…");
  foreach(var device in Session.State.Devices){
   ImGui.PushID(device.Id);try{
    var own=device.Id==vault.Identity.Id;ImGui.TextUnformatted(own?"This device":device.Role=="master"?"Master device":"Pet device");
    ImGui.TextColored(Style.Muted,own?"Your protected chat identity":Session.DeviceVerified(device)?"Verified":"Needs verification");
    var fingerprint=PrivateChatCrypto.Fingerprint(device);ImGui.TextWrapped(fingerprint);if(ImGui.Button("Copy fingerprint"))ImGui.SetClipboardText(fingerprint);
    if(!own){ImGui.SameLine();if(Session.DeviceVerified(device)){if(ImGui.Button("Share available conversation access"))Session.ShareKeys(device);}else if(ImGui.Button("Verify this device…"))OpenAction("verify",device:device);}
    ImGui.Separator();
   }finally{ImGui.PopID();}
  }
  ImGui.TextWrapped("A device can share only the conversations it can already open. To recover older conversations, open an original device or restore its encrypted backup. New devices never receive private keys from the server.");
 }
 private void BackupSettings()
 {
  var vault=Session.Vault!;Style.Title("Encrypted backup","Keep an encrypted backup if you want to restore this device's conversation access later.");
  ImGui.SetNextItemWidth(260*ImGuiHelpers.GlobalScale);ImGui.InputText("Backup password",ref backupPassword,200,ImGuiInputTextFlags.Password);EditingText|=ImGui.IsItemActive();
  ImGui.BeginDisabled(backupPassword.Length<8);try{if(ImGui.Button("Export encrypted backup"))files.SaveFileDialog("Encrypted chat backup",".json","PetService-chat-backup",".json",(ok,path)=>{if(ok)try{File.WriteAllText(path,PrivateChatCrypto.Backup(vault,backupPassword));backupPassword="";}catch(Exception e){Session.ReportError("exporting a chat backup",e);}});}finally{ImGui.EndDisabled();}
  ImGui.SameLine();if(ImGui.Button("Restore encrypted backup"))files.OpenFileDialog("Restore chat backup",".json",(ok,path)=>{if(ok)try{if(new FileInfo(path).Length>5000000)throw new IOException("Backup is too large.");Session.Import(File.ReadAllText(path),backupPassword);backupPassword="";}catch(Exception e){Session.ReportError("restoring a chat backup",e);}});
  ImGui.Spacing();Style.Title("Conversation history","Hold Ctrl+Shift while choosing a permanent action, then confirm in its dialog.");
  var selected=Session.State.Threads.FirstOrDefault(t=>t.Id==Session.Room&&!t.Deleted);ImGui.BeginDisabled(selected is null);
  try{if(ImGui.Button("Delete selected conversation for both…")&&Chord()&&selected is not null)OpenAction("delete",selected,Session.Title(selected));if(master&&ImGui.Button("Clear selected history…")&&Chord()&&selected is not null)OpenAction("clear",selected,Session.Title(selected));}finally{ImGui.EndDisabled();}
  if(master&&ImGui.Button("Clear all conversations for "+pet+"…")&&Chord())OpenAction("all");
 }
 private bool Chord(){if(ImGui.GetIO().KeyCtrl&&ImGui.GetIO().KeyShift)return true;Session.Notice="Hold Ctrl+Shift while clicking the action, then confirm.";return false;}
 private void OpenAction(string kind,ChatThread? thread=null,string name="",ChatDevice? device=null)
 {
  dialog=new(kind,thread?.Id??"",thread?.Generation??0,name,Session.Vault?.Scope??"",Session.Master,device);dialogName=kind=="rename"?name:"";dialogError="";verifiedFingerprint=false;openDialog=true;
 }
 private void DrawDialog()
 {
  if(dialog is null)return;var target=dialog;
  if(target.Scope!=(Session.Vault?.Scope??"")||target.Master!=Session.Master){dialog=null;return;}
  var title=target.Kind switch{"rename"=>"Rename conversation","verify"=>"Verify chat device","delete"=>"Delete conversation for both",_=>"Clear conversation history"};title+="###PetServiceChatAction";
  if(openDialog){ImGui.OpenPopup(title);openDialog=false;}
  ImGui.SetNextWindowSize(new(470*ImGuiHelpers.GlobalScale,0),ImGuiCond.Appearing);var open=true;
  if(!ImGui.BeginPopupModal(title,ref open,ImGuiWindowFlags.AlwaysAutoResize)){if(!open)dialog=null;return;}
  try{
   var writing=target.Kind=="rename";
   if(writing){ImGui.TextWrapped("Choose a new name for this conversation.");if(ImGui.IsWindowAppearing())ImGui.SetKeyboardFocusHere();ImGui.SetNextItemWidth(-1);ImGui.InputTextWithHint("##ConversationName","Conversation name",ref dialogName,120);EditingText|=ImGui.IsItemActive();}
   else if(target.Kind=="verify"){ImGui.TextWrapped("Compare this fingerprint with the device shown to the other participant.");ImGui.TextWrapped(PrivateChatCrypto.Fingerprint(target.Device!));ImGui.Checkbox("I compared this fingerprint",ref verifiedFingerprint);}
   else ImGui.TextWrapped(target.Kind=="delete"?"Delete “"+target.Name+"” and its messages for both participants?":target.Kind=="all"?"Clear every conversation for this profile for both participants?":"Clear the messages in “"+target.Name+"” for both participants?");
   if(dialogError.Length>0)ImGui.TextWrapped(dialogError);
   ImGui.Spacing();ImGui.BeginDisabled(Session.Paused||writing&&string.IsNullOrWhiteSpace(dialogName)||target.Kind=="verify"&&!verifiedFingerprint);
   try{if(ImGui.Button(target.Kind switch{"rename"=>"Save name","verify"=>"Verify and share access","delete"=>"Delete for both",_=>"Confirm permanent clear"})){
    try{
     if(target.Id.Length>0&&Session.State.Threads.FirstOrDefault(t=>t.Id==target.Id&&!t.Deleted)?.Generation!=target.Generation)throw new InvalidOperationException("This conversation changed. Close this dialog and review it first.");
     switch(target.Kind){case "rename":Session.RenameConversation(target.Id,dialogName.Trim());break;case "delete":Session.DeleteConversation(target.Id,target.Generation);break;case "verify":Session.Approve(target.Device!);break;default:if(target.Kind=="all")Session.Clear(false,true);else{Session.Choose(target.Id);Session.Clear(false);}break;}
     dialog=null;ImGui.CloseCurrentPopup();
    }catch(Exception e){dialogError=e.Message;Session.ReportError("a confirmed chat action",e);}
   }}finally{ImGui.EndDisabled();}
   ImGui.SameLine();if(ImGui.Button("Cancel")){dialog=null;ImGui.CloseCurrentPopup();}
  }finally{ImGui.EndPopup();}
 }
 public override void OnClose(){EditingText=false;dialog=null;try{Session.StoreDraft();var size=lastSize;plugin.Mutate(c=>{c.ChatWindow.Width=size.X;c.ChatWindow.Height=size.Y;});}catch{}}
 public void Dispose(){try{Session.Dispose();}finally{try{files.Reset();}finally{password=backupPassword="";}}}
}
