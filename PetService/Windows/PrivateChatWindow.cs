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
 private bool master,settings,sized,openDialog,focusComposer;private string settingsTab="General";
 private Vector2 lastSize;private DialogTarget? dialog;
 private sealed record DialogTarget(string Kind,string Id,int Generation,string Name,string Scope,bool Master);
 internal bool EditingText{get;private set;}
 internal PrivateChatWindow(Plugin plugin):base("Private RP chat###PetServicePrivateChat")
 {this.plugin=plugin;Session=new(plugin);Session.OpenConversationRequested=id=>{settings=false;dialog=null;IsOpen=true;Session.Choose(id);focusComposer=true;};SizeConstraints=new(){MinimumSize=new(700,480),MaximumSize=new(float.MaxValue)};}
 internal void OpenPet(){master=false;settings=false;dialog=null;Session.SetAccount(false,plugin.Configuration.PetName,"");IsOpen=true;}
 internal void OpenMaster(string name,string secret){master=true;pet=name;password="";settings=false;dialog=null;Session.SetAccount(true,name,secret);IsOpen=true;}
 internal void WatchMaster(string name,string secret){if(!IsOpen&&secret.Length>0&&!Session.MatchesAccount(true,name,secret)){master=true;pet=name;Session.SetAccount(true,name,secret);}}
 internal int UnreadFor(bool asMaster,string name)=>Session.Master==asMaster&&Session.Pet==name?Session.UnreadCount:0;
 internal void OpenMasterDefault(){master=true;settings=false;dialog=null;if(!Session.Master)Session.SetAccount(true,pet,"");IsOpen=true;}
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
  lastSize=ImGui.GetWindowSize();EditingText=ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);Session.ViewingConversation=!settings&&EditingText&&!ImGui.GetIO().AppFocusLost;Session.Tick(!ImGui.GetIO().AppFocusLost);files.Draw();
  if(master&&Session.Vault is null&&!Session.HasCredential){Style.Title("Master RP chat","Unlock this chat session.");ImGui.InputText("Pet profile",ref pet,32);ImGui.InputText("Master password",ref password,4096,ImGuiInputTextFlags.Password);if(ImGui.Button("Unlock chat")&&pet.Length>0&&password.Length>0){Session.SetAccount(true,pet,password);password="";}return;}
  Style.Title("PRIVATE RP CHAT",(master?pet:plugin.Configuration.PetName)+(Session.UnreadCount>0?" · "+Session.UnreadCount+" unread":""));
  if(ImGui.Button(settings?"Back to conversations":"Chat settings"))settings=!settings;
  if(master){ImGui.SameLine();if(ImGui.Button("Lock chat")){Session.Lock();dialog=null;return;}}
  ImGui.SameLine();ImGui.TextColored(Style.Muted,Session.Paused?"Chat paused for character access":!Session.Ready?"Connecting…":Session.Preferences.GlobalPaused?"My chat is paused":"Connected · encrypted");
  ImGui.Separator();
  if(Session.Paused){ImGui.TextWrapped(plugin.IsPaired?"Private chat is paused on this unapproved character. Return to an approved character to resume.":"Pair this device on Pet → Setup to use private chat.");return;}
  if(Session.Vault is null){ImGui.TextWrapped(Session.Error.Length>0?Session.Error:"Opening protected chat storage…");return;}
  if(settings)DrawSettings();else{DrawChat();Session.MarkRead();}
  ImGui.Separator();
  if(Session.Error.Length>0)ImGui.TextWrapped(Session.Error);
  else ImGui.TextWrapped(Session.PreferencesSaving?"Saving chat preferences…":Session.Notice.Length>0?Session.Notice:"Enter sends · Shift+Enter adds a line");
 }
 private string Label(ChatThread thread,int index){var title=Session.Title(thread);return title=="Conversation"?"Conversation "+(index+1):title;}
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
     var suffix=(Session.IsUnread(thread.Id)?" · unread":"")+(Session.DeletionPending(thread.Id)?" · deleting…":Session.Preferences.Paused.Contains(thread.Id)?" · paused":Session.Preferences.Muted.Contains(thread.Id)?" · muted":"");
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
  var issue=Session.ConversationIssue(thread);if(issue.Length>0)ImGui.TextColored(Style.Muted,"Synchronizing encrypted history…");
  if(Session.ConversationPaused(thread.Id)){ImGui.TextColored(Style.Muted,Session.Preferences.GlobalPaused?"My RP chat is paused. Existing history remains readable.":"This conversation is paused. Existing history remains readable.");}
  var available=ImGui.GetContentRegionAvail().Y;var height=Math.Clamp(plugin.Configuration.ChatWindow.Composer,60,Math.Max(60,available*.55f));
  var visible=ImGui.BeginChild("RP transcript",new(0,Math.Max(70,available-height-110)),true);
  try{if(visible){if(Session.More){ImGui.BeginDisabled(Session.Busy);try{if(ImGui.SmallButton("Load older messages"))Session.Poll(true);}finally{ImGui.EndDisabled();}}var bottom=ImGui.GetScrollY()>=ImGui.GetScrollMaxY()-8;transcript.Draw(Session.Messages);if(bottom)ImGui.SetScrollHereY(1);}}finally{ImGui.EndChild();}
  ImGui.InvisibleButton("Resize message box",new(-1,8));if(ImGui.IsItemHovered()||ImGui.IsItemActive())ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);if(ImGui.IsItemActive())plugin.Configuration.ChatWindow.Composer=Math.Clamp(height-ImGui.GetIO().MouseDelta.Y,60,Math.Max(60,available*.55f));
  var vault=Session.Vault!;ImGui.SetNextItemWidth(220*ImGuiHelpers.GlobalScale);
  if(ImGui.BeginCombo("Speaking as",vault.Personas.FirstOrDefault(p=>p.Id==vault.Active)?.Name??vault.Personas.FirstOrDefault()?.Name??"Choose name")){try{foreach(var persona in vault.Personas)if(ImGui.Selectable(persona.Name,persona.Id==vault.Active)){Session.ChoosePersona(persona.Id);focusComposer=true;}}finally{ImGui.EndCombo();}}
  ImGui.BeginDisabled(Session.Paused||Session.ConversationPaused(Session.Room)||Session.DeletionPending(Session.Room));
  try{
   var text=Session.Draft;var io=ImGui.GetIO();var ctrl=io.KeyCtrl;bool enter;
   try{if(focusComposer){if(!ImGui.IsMouseClicked(ImGuiMouseButton.Left)&&!ImGui.IsMouseClicked(ImGuiMouseButton.Right))ImGui.SetKeyboardFocusHere();focusComposer=false;}if(io.KeyShift&&ImGui.IsKeyDown(ImGuiKey.Enter))io.KeyCtrl=true;enter=ImGui.InputTextMultiline("##RP composer",ref text,10000,new(-1,height),ImGuiInputTextFlags.EnterReturnsTrue|ImGuiInputTextFlags.CtrlEnterForNewLine);}finally{io.KeyCtrl=ctrl;}
   EditingText|=ImGui.IsItemActive();if(text!=Session.Draft){Session.Draft=text;Session.StoreDraft();}if(enter){Session.Send();focusComposer=true;}if(ImGui.Button("Send message")){Session.Send();focusComposer=true;}ImGui.SameLine();ImGui.TextColored(Style.Muted,"Enter sends · Shift+Enter adds a line");
  }finally{ImGui.EndDisabled();}
 }
 private void DrawSettings()
 {
  if(!ImGui.BeginTabBar("Private chat settings"))return;
  try{
   foreach(var page in new[]{"General","Names","Backup and history"}){
    var flags=settingsTab==page?ImGuiTabItemFlags.SetSelected:ImGuiTabItemFlags.None;
    if(!ImGui.BeginTabItem(page,flags))continue;
    settingsTab="";try{switch(page){case "General":GeneralSettings();break;case "Names":NameSettings();break;default:BackupSettings();break;}}finally{ImGui.EndTabItem();}
   }
  }finally{ImGui.EndTabBar();}
 }
 private void GeneralSettings()
 {
  Style.Title("My chat connection","These controls affect your side of private RP chat.");var pause=Session.Preferences.GlobalPaused;
  ImGui.BeginDisabled(!Session.Ready);try{if(ImGui.Checkbox("Pause my RP chat",ref pause))Session.Preference(global:pause);}finally{ImGui.EndDisabled();}
  ImGui.TextWrapped("Pausing holds new sending and receiving in all of your conversations. The other participant can keep writing; their messages arrive when you resume.");
  ImGui.Spacing();Style.Title("Startup","Choose which private RP chat window opens when you log in.");DrawLoginSettings();
  ImGui.Spacing();Style.Title("Notifications","Unread markers remain until you view the conversation. Muting hides notices without stopping messages.");var toasts=plugin.Configuration.PrivateChatToastNotifications;if(ImGui.Checkbox("Show chat popup notifications",ref toasts))plugin.Mutate(c=>c.PrivateChatToastNotifications=toasts);var log=plugin.Configuration.PrivateChatLogNotifications;if(ImGui.Checkbox("Show generic notices in game chat",ref log))plugin.Mutate(c=>c.PrivateChatLogNotifications=log);
  ImGui.Spacing();Style.Title("Conversation controls","Right-click a conversation to rename, mute, pause or delete it.");
  ImGui.TextWrapped("Mute notifications keeps messages arriving without new-message alerts. Pause conversation holds sending and receiving for that conversation. These preferences synchronize across your devices.");
  ImGui.Spacing();ImGui.TextWrapped("The gameplay kill switch and Sleep do not pause RP chat. An unapproved character pauses the pet's chat connection.");
 }
 internal void DrawLoginSettings()
 {
  var role=plugin.Configuration.PrivateChatLoginRole;var label=role switch{ChatLoginRole.Pet=>"Pet",ChatLoginRole.Master=>"Master",_=>"Off"};
  ImGui.SetNextItemWidth(190*ImGuiHelpers.GlobalScale);
  if(ImGui.BeginCombo("Open RP chat on character login",label)){
   try{foreach(var choice in Enum.GetValues<ChatLoginRole>())if(ImGui.Selectable(choice.ToString(),role==choice))plugin.Mutate(c=>c.PrivateChatLoginRole=choice);}
   finally{ImGui.EndCombo();}
  }
  ImGui.TextWrapped("Opens once after your character finishes loading. Changes apply at the next character login.");
 }
 private void NameSettings()
 {
  var vault=Session.Vault!;Style.Title("Speaking names","Names and colors synchronize securely across your devices. Choose one above the message box while writing. Earlier messages keep their original name and color.");
  foreach(var persona in vault.Personas.ToList()){
   ImGui.PushID(persona.Id);try{var name=persona.Name;ImGui.SetNextItemWidth(250*ImGuiHelpers.GlobalScale);if(ImGui.InputText("Name",ref name,60)){persona.Name=name;Session.SaveNames();}EditingText|=ImGui.IsItemActive();var color=persona.Color;if(Style.ColorField("Name color",ref color)){persona.Color=color;Session.SaveNames();}ImGui.BeginDisabled(vault.Personas.Count<=1);try{if(ImGui.Button("Remove name")){vault.Personas.Remove(persona);Session.SaveNames();}}finally{ImGui.EndDisabled();}ImGui.Separator();}finally{ImGui.PopID();}
  }
  if(vault.Personas.Count<40&&ImGui.Button("+ Add speaking name")){vault.Personas.Add(new());Session.SaveNames();}
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
 private void OpenAction(string kind,ChatThread? thread=null,string name="")
 {
  dialog=new(kind,thread?.Id??"",thread?.Generation??0,name,Session.Vault?.Scope??"",Session.Master);dialogName=kind=="rename"?name:"";dialogError="";openDialog=true;
 }
 private void DrawDialog()
 {
  if(dialog is null)return;var target=dialog;
  if(target.Scope!=(Session.Vault?.Scope??"")||target.Master!=Session.Master){dialog=null;return;}
  var title=target.Kind switch{"rename"=>"Rename conversation","delete"=>"Delete conversation for both",_=>"Clear conversation history"};title+="###PetServiceChatAction";
  if(openDialog){ImGui.OpenPopup(title);openDialog=false;}
  ImGui.SetNextWindowSize(new(470*ImGuiHelpers.GlobalScale,0),ImGuiCond.Appearing);var open=true;
  if(!ImGui.BeginPopupModal(title,ref open,ImGuiWindowFlags.AlwaysAutoResize)){if(!open)dialog=null;return;}
  try{
   var writing=target.Kind=="rename";
   if(writing){ImGui.TextWrapped("Choose a new name for this conversation.");if(ImGui.IsWindowAppearing())ImGui.SetKeyboardFocusHere();ImGui.SetNextItemWidth(-1);ImGui.InputTextWithHint("##ConversationName","Conversation name",ref dialogName,120);EditingText|=ImGui.IsItemActive();}
   else ImGui.TextWrapped(target.Kind=="delete"?"Delete “"+target.Name+"” and its messages for both participants?":target.Kind=="all"?"Clear every conversation for this profile for both participants?":"Clear the messages in “"+target.Name+"” for both participants?");
   if(dialogError.Length>0)ImGui.TextWrapped(dialogError);
   ImGui.Spacing();ImGui.BeginDisabled(Session.Paused||writing&&string.IsNullOrWhiteSpace(dialogName));
   try{if(ImGui.Button(target.Kind switch{"rename"=>"Save name","delete"=>"Delete for both",_=>"Confirm permanent clear"})){
    try{
     if(target.Id.Length>0&&Session.State.Threads.FirstOrDefault(t=>t.Id==target.Id&&!t.Deleted)?.Generation!=target.Generation)throw new InvalidOperationException("This conversation changed. Close this dialog and review it first.");
     switch(target.Kind){case "rename":Session.RenameConversation(target.Id,dialogName.Trim());break;case "delete":Session.DeleteConversation(target.Id,target.Generation);break;default:if(target.Kind=="all")Session.Clear(false,true);else{Session.Choose(target.Id);Session.Clear(false);}break;}
     dialog=null;ImGui.CloseCurrentPopup();
    }catch(Exception e){dialogError=e.Message;Session.ReportError("a confirmed chat action",e);}
   }}finally{ImGui.EndDisabled();}
   ImGui.SameLine();if(ImGui.Button("Cancel")){dialog=null;ImGui.CloseCurrentPopup();}
  }finally{ImGui.EndPopup();}
 }
 public override void OnClose(){Session.ViewingConversation=false;EditingText=false;dialog=null;try{Session.StoreDraft();var size=lastSize;plugin.Mutate(c=>{c.ChatWindow.Width=size.X;c.ChatWindow.Height=size.Y;});}catch{}}
 public void Dispose(){try{Session.Dispose();}finally{try{files.Reset();}finally{password=backupPassword="";}}}
}
