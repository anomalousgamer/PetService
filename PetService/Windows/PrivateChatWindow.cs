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
 private string password="",pet="",backupPassword="",confirm="";private bool master,settings,sized;
 private ChatDevice? approve;private Vector2 lastSize;
 internal bool EditingText{get;private set;}
 internal PrivateChatWindow(Plugin plugin):base("Private RP chat###PetServicePrivateChat"){this.plugin=plugin;Session=new(plugin);SizeConstraints=new(){MinimumSize=new(640,430),MaximumSize=new(float.MaxValue)};}
 internal void OpenPet(){master=false;settings=false;Session.SetAccount(false,plugin.Configuration.PetName,"");IsOpen=true;}
 internal void OpenMaster(string name,string secret){master=true;pet=name;password="";settings=false;Session.SetAccount(true,name,secret);IsOpen=true;}
 internal void OpenDefault(){if(master){settings=false;IsOpen=true;}else OpenPet();}
 public override void PreDraw(){if(!sized){Size=new(plugin.Configuration.ChatWindow.Width,plugin.Configuration.ChatWindow.Height);SizeCondition=ImGuiCond.FirstUseEver;sized=true;}Style.ApplyTheme(plugin.Configuration.Features.Identity.Theme);Style.Push();}
 public override void PostDraw()=>Style.Pop();
 public override void Draw()
 {
  plugin.DrawingPrivateChat=true;
  try{DrawContents();}catch(Exception e){Session.ReportError("drawing the chat window",e);ImGui.TextWrapped("Private chat could not complete this action. "+e.Message);}finally{plugin.DrawingPrivateChat=false;}
 }
 private void DrawContents()
 {
  lastSize=ImGui.GetWindowSize();EditingText=ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);Session.Tick(!ImGui.GetIO().AppFocusLost);files.Draw();
  try{
   if(master&&Session.Vault is null&&!Session.HasCredential){ImGui.InputText("Pet profile",ref pet,32);EditingText|=ImGui.IsItemActive();ImGui.InputText("Master password",ref password,4096,ImGuiInputTextFlags.Password);EditingText|=ImGui.IsItemActive();if(ImGui.Button("Unlock chat")&&pet.Length>0&&password.Length>0){Session.SetAccount(true,pet,password);password="";}if(!Session.HasCredential)return;}
   ImGui.TextColored(Style.Accent,"Private chat · "+(master?pet:plugin.Configuration.PetName));ImGui.SameLine();if(ImGui.SmallButton(settings?"Hide settings":"Settings"))settings=!settings;
   if(master&&ImGui.SmallButton("Lock chat")){Session.Lock();return;}
   if(Session.Paused){ImGui.TextWrapped(plugin.IsPaired?"Private chat is paused on this unapproved character. Return to an approved character to resume.":"Pair this device on Pet → Setup to use private chat.");return;}
   if(settings){DrawSettings();if(Session.Error.Length>0)ImGui.TextWrapped(Session.Error);return;}
   if(Session.Vault is null){ImGui.TextWrapped(Session.Paused?"Chat is paused. Resume the connection or approve this character to receive messages.":Session.Error.Length>0?Session.Error:"Opening protected chat storage…");return;}
   if(ImGui.BeginTable("chat-panes",2,ImGuiTableFlags.Resizable|ImGuiTableFlags.SizingStretchProp)){
    try{
    ImGui.TableSetupColumn("Conversations",ImGuiTableColumnFlags.WidthFixed,180*ImGuiHelpers.GlobalScale);ImGui.TableSetupColumn("Transcript",ImGuiTableColumnFlags.WidthStretch);ImGui.TableNextColumn();
    var title=Session.TitleDraft;if(ImGui.InputText("##new-thread-title",ref title,120))Session.TitleDraft=title;EditingText|=ImGui.IsItemActive();ImGui.BeginDisabled(!Session.Ready||Session.MutationBusy);try{if(ImGui.Button("New conversation"))Session.Create();}finally{ImGui.EndDisabled();}
    foreach(var thread in Session.State.Threads.Where(t=>!t.Deleted)){ImGui.PushID(thread.Id);try{if(ImGui.Selectable(Session.Title(thread)+(Session.ConversationPaused(thread.Id)?" [paused]":Session.Preferences.Muted.Contains(thread.Id)?" [muted]":""),Session.Room==thread.Id))Session.Choose(thread.Id);ConversationMenu(thread);}finally{ImGui.PopID();}}
    ImGui.TableNextColumn();DrawConversation();
    }finally{ImGui.EndTable();}
   }
   if(Session.Error.Length>0)ImGui.TextWrapped(Session.Error);else ImGui.TextColored(Style.Muted,Session.Paused?"Chat paused for character access":!Session.Ready?"Connecting to encrypted chat…":Session.Preferences.GlobalPaused?"All private conversations paused":Session.Preferences.Paused.Contains(Session.Room)?"This conversation is paused":Session.Notice.Length>0?Session.Notice:"Connected · encrypted");
  }catch(Exception e){Session.ReportError("a chat action",e);ImGui.TextWrapped("Chat could not complete that action. "+e.Message);}
 }
 private void ConversationMenu(ChatThread thread)
 {
  if(!ImGui.BeginPopupContextItem("Conversation actions"))return;
  try{
  if(ImGui.MenuItem(Session.Preferences.Muted.Contains(thread.Id)?"Unmute alerts":"Mute alerts"))Session.Preference(thread.Id,muted:!Session.Preferences.Muted.Contains(thread.Id));
  if(ImGui.MenuItem(Session.Preferences.Paused.Contains(thread.Id)?"Resume conversation":"Pause conversation"))Session.Preference(thread.Id,paused:!Session.Preferences.Paused.Contains(thread.Id));
  if(ImGui.MenuItem("Rename conversation")){Session.Choose(thread.Id);Session.TitleDraft=Session.Title(thread);settings=true;}
  if(ImGui.MenuItem("Delete for both… (Ctrl+Shift)")){if(ImGui.GetIO().KeyCtrl&&ImGui.GetIO().KeyShift){Session.Choose(thread.Id);confirm="delete";settings=true;}else Session.Notice="Hold Ctrl+Shift while selecting Delete, then confirm.";}
  }finally{ImGui.EndPopup();}
 }
 private void DrawConversation()
 {
  if(Session.State.Threads.FirstOrDefault(t=>t.Id==Session.Room&&!t.Deleted) is { } selected && Session.ConversationIssue(selected) is {Length:>0} issue){Style.Title("Conversation locked",issue);ImGui.TextWrapped("Open Chat Settings to compare device fingerprints. The participant who already has this conversation can approve this device and share its keys.");if(ImGui.Button("Open Chat Settings"))settings=true;return;}
  var available=ImGui.GetContentRegionAvail().Y;var height=Math.Clamp(plugin.Configuration.ChatWindow.Composer,60,Math.Max(60,available*.55f));
  var transcriptVisible=ImGui.BeginChild("transcript",new Vector2(0,Math.Max(70,available-height-100)),true);
  try{if(transcriptVisible){
   if(Session.More&&!Session.Busy&&ImGui.SmallButton("Load older messages"))Session.Poll(true);var bottom=ImGui.GetScrollY()>=ImGui.GetScrollMaxY()-8;
   transcript.Draw(Session.Messages);if(bottom)ImGui.SetScrollHereY(1);
  }}finally{ImGui.EndChild();}
  ImGui.InvisibleButton("Resize message box",new Vector2(-1,8));if(ImGui.IsItemHovered()||ImGui.IsItemActive())ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);if(ImGui.IsItemActive())plugin.Configuration.ChatWindow.Composer=Math.Clamp(height-ImGui.GetIO().MouseDelta.Y,60,Math.Max(60,available*.55f));
  var vault=Session.Vault!;if(ImGui.BeginCombo("Speaking as",vault.Personas.FirstOrDefault(p=>p.Id==vault.Active)?.Name??vault.Personas.FirstOrDefault()?.Name??"Choose name")){try{foreach(var persona in vault.Personas)if(ImGui.Selectable(persona.Name,persona.Id==vault.Active)){vault.Active=persona.Id;Session.Save();}}finally{ImGui.EndCombo();}}
  ImGui.BeginDisabled(Session.Room.Length==0||Session.Paused||Session.ConversationPaused(Session.Room));try{var text=Session.Draft;var io=ImGui.GetIO();var ctrl=io.KeyCtrl;bool enter;
  try{if(io.KeyShift&&ImGui.IsKeyDown(ImGuiKey.Enter))io.KeyCtrl=true;enter=ImGui.InputTextMultiline("##RP composer",ref text,10000,new Vector2(-1,height),ImGuiInputTextFlags.EnterReturnsTrue|ImGuiInputTextFlags.CtrlEnterForNewLine);}finally{io.KeyCtrl=ctrl;}
  EditingText|=ImGui.IsItemActive();if(text!=Session.Draft){Session.Draft=text;Session.StoreDraft();}if(enter)Session.Send();if(ImGui.Button("Send")&&!string.IsNullOrWhiteSpace(Session.Draft))Session.Send();}finally{ImGui.EndDisabled();}ImGui.SameLine();ImGui.TextColored(Style.Muted,"Enter sends · Shift+Enter adds a line");
 }
 private void DrawSettings()
 {
  var vault=Session.Vault;if(vault is null)return;Style.Title("Personal chat controls","Mute suppresses visual alerts. Pause holds sending and receiving on your side; the other participant may keep writing. These settings synchronize across your devices.");var global=Session.Preferences.GlobalPaused;ImGui.BeginDisabled(!Session.Ready||Session.MutationBusy);if(ImGui.Checkbox("Pause all of my private conversations",ref global))Session.Preference(global:global);ImGui.EndDisabled();ImGui.TextWrapped("The kill switch does not affect RP chat. Unapproved characters pause the pet chat connection.");Style.Title("Names and colors","Your saved names apply across conversations; existing messages keep their original name and color.");
  foreach(var persona in vault.Personas.ToList()){ImGui.PushID(persona.Id);try{var name=persona.Name;if(ImGui.InputText("Name",ref name,60)){persona.Name=name;Session.Save();}EditingText|=ImGui.IsItemActive();var color=persona.Color;if(Style.ColorField("Name color",ref color)){persona.Color=color;Session.Save();}if(vault.Personas.Count>1&&ImGui.SmallButton("Remove name")){vault.Personas.Remove(persona);Session.Save();}ImGui.Separator();}finally{ImGui.PopID();}}if(vault.Personas.Count<40&&ImGui.Button("Add name")){vault.Personas.Add(new());Session.Save();}
  Style.Title("Device verification","Compare fingerprints using a separate trusted channel before approving. An approved device can receive conversation history.");
  foreach(var device in Session.State.Devices){ImGui.PushID(device.Id);try{ImGui.TextWrapped(device.Role+" · "+PrivateChatCrypto.Fingerprint(device));if(device.Id!=vault.Identity.Id&&ImGui.Button("Approve and share keys")){approve=device;ImGui.OpenPopup("Confirm device");}if(ImGui.BeginPopup("Confirm device")){try{ImGui.TextWrapped("Have you compared this fingerprint with the other device?");if(ImGui.Button("Approve verified device")&&approve is not null){Session.Approve(approve);ImGui.CloseCurrentPopup();}}finally{ImGui.EndPopup();}}}finally{ImGui.PopID();}}
  ImGui.InputText("Backup password",ref backupPassword,200,ImGuiInputTextFlags.Password);EditingText|=ImGui.IsItemActive();ImGui.BeginDisabled(backupPassword.Length<8);if(ImGui.Button("Export encrypted backup")){files.SaveFileDialog("Encrypted chat backup",".json","PetService-chat-backup",".json",(ok,path)=>{if(ok)try{File.WriteAllText(path,PrivateChatCrypto.Backup(vault,backupPassword));backupPassword="";}catch{Session.Error="Could not save backup.";}});}ImGui.EndDisabled();if(ImGui.Button("Restore encrypted backup"))files.OpenFileDialog("Restore chat backup",".json",(ok,path)=>{if(ok)try{var file=new FileInfo(path);if(file.Length>5000000)throw new InvalidOperationException();Session.Import(File.ReadAllText(path),backupPassword);backupPassword="";}catch{Session.Error="Could not open backup. Check its password and participant.";}});
  if(Session.Room.Length>0){var title=Session.TitleDraft;if(ImGui.InputText("Conversation title",ref title,120))Session.TitleDraft=title;EditingText|=ImGui.IsItemActive();if(ImGui.Button("Rename selected conversation"))Session.Rename();}
  Style.Title("Delete conversation","Either participant can delete one conversation for both people. Hold Ctrl+Shift and click, then confirm. Copied text and backups cannot be recalled.");Danger("Delete selected conversation for both","delete");
  if(master){Danger("Clear selected history","clear");Danger("Clear ALL conversations for "+pet,"all");}
  if(confirm.Length>0){ImGui.TextWrapped("Confirm "+confirm+" for "+(master?pet:plugin.Configuration.PetName)+"? This affects both participants.");if(ImGui.Button("Confirm permanent deletion")){Session.Clear(confirm=="delete",confirm=="all");confirm="";}ImGui.SameLine();if(ImGui.Button("Cancel"))confirm="";}
 }
 private void Danger(string label,string action){ImGui.BeginDisabled(Session.MutationBusy||action!="all"&&Session.Room.Length==0);if(ImGui.Button(label)&&ImGui.GetIO().KeyCtrl&&ImGui.GetIO().KeyShift)confirm=action;ImGui.EndDisabled();}
 public override void OnClose(){EditingText=false;try{Session.StoreDraft();var size=lastSize;plugin.Mutate(c=>{c.ChatWindow.Width=size.X;c.ChatWindow.Height=size.Y;});}catch{}}
 public void Dispose(){try{Session.Dispose();}finally{try{files.Reset();}finally{password=backupPassword="";}}}
}
