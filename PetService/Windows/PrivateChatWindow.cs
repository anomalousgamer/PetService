using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.ImGuiFileDialog;
using PetService.Core;
namespace PetService.Windows;
internal sealed class PrivateChatWindow:Window,IDisposable
{
 private readonly Plugin plugin;internal readonly PrivateChatSession Session;private readonly ChatTranscript transcript=new();private readonly FileDialogManager files=new();private string password="",pet="",backupPassword="",soundName="";private bool master,settings,sounds;private string confirm="";private ChatDevice? approve;private SoundCatalog catalog=new();private Task<JsonElement>? audioTask;private Action<JsonElement>? audioDone;private byte[]? upload;private string replaceSound="";private bool sized;private Vector2 lastSize;private readonly LocalSoundPlayer preview=new();private Task<byte[]>? previewTask;private bool reloadSounds;
 private void ResetAudio(){if(audioTask is not null)_=audioTask.ContinueWith(t=>{_=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);audioTask=null;audioDone=null;previewTask=null;preview.Dispose();catalog=new();upload=null;replaceSound="";confirm="";}
 internal bool EditingText{get;private set;}
 internal PrivateChatWindow(Plugin plugin):base("Private RP chat###PetServicePrivateChat"){this.plugin=plugin;Session=new(plugin);SizeConstraints=new(){MinimumSize=new(640,430),MaximumSize=new(float.MaxValue)};}
 internal void OpenPet(){if(master)ResetAudio();master=false;sounds=false;settings=false;Session.SetAccount(false,plugin.Configuration.PetName,"");IsOpen=true;}
 internal void OpenMaster(string name,string secret,bool audio=false){if(!master||Session.Pet!=name)ResetAudio();master=true;pet=name;password="";sounds=audio;settings=false;reloadSounds=audio;Session.SetAccount(true,name,secret);IsOpen=true;}
 internal void OpenDefault(){if(master){sounds=false;settings=false;IsOpen=true;}else OpenPet();}
 public override void PreDraw(){if(!sized){Size=new(plugin.Configuration.ChatWindow.Width,plugin.Configuration.ChatWindow.Height);SizeCondition=ImGuiCond.FirstUseEver;sized=true;}Style.ApplyTheme(plugin.Configuration.Features.Identity.Theme);Style.Push();}
 public override void PostDraw()=>Style.Pop();
 public override void Draw()
 {
  plugin.DrawingPrivateChat=true;
  try{DrawContents();}catch(Exception e){Session.Error=e.Message;ImGui.TextWrapped("Private chat could not complete this action. "+e.Message);}finally{plugin.DrawingPrivateChat=false;}
 }
 private void DrawContents()
 {
  lastSize=ImGui.GetWindowSize();EditingText=ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);Session.Tick(!ImGui.GetIO().AppFocusLost);files.Draw();
  if(audioTask is {IsCompleted:true}){var t=audioTask;var done=audioDone;audioTask=null;audioDone=null;try{done?.Invoke(t.GetAwaiter().GetResult());}catch(Exception e){Session.Error=e.Message;}}
  if(previewTask is {IsCompleted:true}){var task=previewTask;previewTask=null;try{preview.Play(task.GetAwaiter().GetResult(),.5f);}catch{Session.Error="Could not preview that sound.";}}
  if(reloadSounds&&Session.HasCredential&&audioTask is null){reloadSounds=false;Audio("sounds",new{},v=>catalog=JsonSerializer.Deserialize<SoundCatalog>(v,ServiceClient.Json)!);}
  try{
   if(master&&Session.Vault is null&&!Session.HasCredential){ImGui.InputText("Pet profile",ref pet,32);EditingText|=ImGui.IsItemActive();ImGui.InputText("Master password",ref password,4096,ImGuiInputTextFlags.Password);EditingText|=ImGui.IsItemActive();if(ImGui.Button("Unlock chat")&&pet.Length>0&&password.Length>0){Session.SetAccount(true,pet,password);password="";}if(!Session.HasCredential)return;}
   ImGui.TextColored(Style.Accent,"Private chat · "+(master?pet:plugin.Configuration.PetName));ImGui.SameLine();if(ImGui.SmallButton(settings?"Hide settings":"Settings"))settings=!settings;ImGui.SameLine();if(master&&ImGui.SmallButton(sounds?"Chat":"Sounds")){sounds=!sounds;if(sounds)Audio("sounds",new{},v=>catalog=JsonSerializer.Deserialize<SoundCatalog>(v,ServiceClient.Json)!);}
   if(master&&ImGui.SmallButton("Lock chat")){ResetAudio();Session.Lock();return;}
   if(Session.Paused){ImGui.TextWrapped(plugin.IsPaired?"Private chat is paused on this unapproved character. Return to an approved character to resume.":"Pair this device on Pet → Setup to use private chat.");return;}
   if(settings){DrawSettings();if(Session.Error.Length>0)ImGui.TextWrapped(Session.Error);return;}
   if(sounds&&master){DrawSounds();if(Session.Error.Length>0)ImGui.TextWrapped(Session.Error);return;}
   if(Session.Vault is null){ImGui.TextWrapped(Session.Paused?"Chat is paused. Resume the connection or approve this character to receive messages.":Session.Error.Length>0?Session.Error:"Opening protected chat storage…");return;}
   if(ImGui.BeginTable("chat-panes",2,ImGuiTableFlags.Resizable|ImGuiTableFlags.SizingStretchProp)){
    ImGui.TableSetupColumn("Conversations",ImGuiTableColumnFlags.WidthFixed,180*ImGuiHelpers.GlobalScale);ImGui.TableSetupColumn("Transcript",ImGuiTableColumnFlags.WidthStretch);ImGui.TableNextColumn();
    var title=Session.TitleDraft;if(ImGui.InputText("##new-thread-title",ref title,120))Session.TitleDraft=title;EditingText|=ImGui.IsItemActive();ImGui.BeginDisabled(Session.Busy);if(ImGui.Button("New conversation"))Session.Create();
    foreach(var thread in Session.State.Threads.Where(t=>!t.Deleted)){ImGui.PushID(thread.Id);if(ImGui.Selectable(Session.Title(thread)+(Session.ConversationPaused(thread.Id)?" [paused]":Session.Preferences.Muted.Contains(thread.Id)?" [muted]":""),Session.Room==thread.Id))Session.Choose(thread.Id);ConversationMenu(thread);ImGui.PopID();}ImGui.EndDisabled();
    ImGui.TableNextColumn();DrawConversation();ImGui.EndTable();
   }
   if(Session.Error.Length>0)ImGui.TextWrapped(Session.Error);else ImGui.TextColored(Style.Muted,Session.Paused?"Chat paused for character access":Session.Preferences.GlobalPaused?"All private conversations paused":Session.Preferences.Paused.Contains(Session.Room)?"This conversation is paused":Session.Notice.Length>0?Session.Notice:"Connected · encrypted");
  }catch(Exception e){Session.Error=e.Message;ImGui.TextWrapped("Chat could not complete that action. "+e.Message);}
 }
 private void ConversationMenu(ChatThread thread)
 {
  if(!ImGui.BeginPopupContextItem("Conversation actions"))return;
  if(ImGui.MenuItem(Session.Preferences.Muted.Contains(thread.Id)?"Unmute alerts":"Mute alerts"))Session.Preference(thread.Id,muted:!Session.Preferences.Muted.Contains(thread.Id));
  if(ImGui.MenuItem(Session.Preferences.Paused.Contains(thread.Id)?"Resume conversation":"Pause conversation"))Session.Preference(thread.Id,paused:!Session.Preferences.Paused.Contains(thread.Id));
  if(ImGui.MenuItem("Rename conversation")){Session.Choose(thread.Id);Session.TitleDraft=Session.Title(thread);settings=true;}
  if(ImGui.MenuItem("Delete for both… (Ctrl+Shift)")){if(ImGui.GetIO().KeyCtrl&&ImGui.GetIO().KeyShift){Session.Choose(thread.Id);confirm="delete";settings=true;}else Session.Notice="Hold Ctrl+Shift while selecting Delete, then confirm.";}
  ImGui.EndPopup();
 }
 private void DrawConversation()
 {
  var available=ImGui.GetContentRegionAvail().Y;var height=Math.Clamp(plugin.Configuration.ChatWindow.Composer,60,Math.Max(60,available*.55f));
  if(ImGui.BeginChild("transcript",new Vector2(0,Math.Max(70,available-height-100)),true)){
   if(Session.More&&!Session.Busy&&ImGui.SmallButton("Load older messages"))Session.Poll(true);var bottom=ImGui.GetScrollY()>=ImGui.GetScrollMaxY()-8;
   transcript.Draw(Session.Messages);if(bottom)ImGui.SetScrollHereY(1);
  }ImGui.EndChild();
  ImGui.InvisibleButton("Resize message box",new Vector2(-1,8));if(ImGui.IsItemHovered()||ImGui.IsItemActive())ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);if(ImGui.IsItemActive())plugin.Configuration.ChatWindow.Composer=Math.Clamp(height-ImGui.GetIO().MouseDelta.Y,60,Math.Max(60,available*.55f));
  var vault=Session.Vault!;if(ImGui.BeginCombo("Speaking as",vault.Personas.FirstOrDefault(p=>p.Id==vault.Active)?.Name??vault.Personas.FirstOrDefault()?.Name??"Choose name")){foreach(var persona in vault.Personas)if(ImGui.Selectable(persona.Name,persona.Id==vault.Active)){vault.Active=persona.Id;Session.Save();}ImGui.EndCombo();}
  ImGui.BeginDisabled(Session.Room.Length==0||Session.Paused||Session.ConversationPaused(Session.Room));var text=Session.Draft;var io=ImGui.GetIO();var ctrl=io.KeyCtrl;bool enter;
  try{if(io.KeyShift&&ImGui.IsKeyDown(ImGuiKey.Enter))io.KeyCtrl=true;enter=ImGui.InputTextMultiline("##RP composer",ref text,10000,new Vector2(-1,height),ImGuiInputTextFlags.EnterReturnsTrue|ImGuiInputTextFlags.CtrlEnterForNewLine);}finally{io.KeyCtrl=ctrl;}
  EditingText|=ImGui.IsItemActive();if(text!=Session.Draft){Session.Draft=text;Session.StoreDraft();}if(enter)Session.Send();if(ImGui.Button("Send")&&!string.IsNullOrWhiteSpace(Session.Draft))Session.Send();ImGui.EndDisabled();ImGui.SameLine();ImGui.TextColored(Style.Muted,"Enter sends · Shift+Enter adds a line");
 }
 private void DrawSettings()
 {
  var vault=Session.Vault;if(vault is null)return;Style.Title("Personal chat controls","Mute suppresses visual alerts. Pause holds sending and receiving on your side; the other participant may keep writing. These settings synchronize across your devices.");var global=Session.Preferences.GlobalPaused;ImGui.BeginDisabled(Session.Busy);if(ImGui.Checkbox("Pause all of my private conversations",ref global))Session.Preference(global:global);ImGui.EndDisabled();ImGui.TextWrapped("The kill switch does not affect RP chat. Unapproved characters pause the pet chat connection.");Style.Title("Names and colors","Your saved names apply across conversations; existing messages keep their original name and color.");
  foreach(var persona in vault.Personas.ToList()){ImGui.PushID(persona.Id);var name=persona.Name;if(ImGui.InputText("Name",ref name,60)){persona.Name=name;Session.Save();}EditingText|=ImGui.IsItemActive();var color=persona.Color;if(Style.ColorField("Name color",ref color)){persona.Color=color;Session.Save();}if(vault.Personas.Count>1&&ImGui.SmallButton("Remove name")){vault.Personas.Remove(persona);Session.Save();}ImGui.Separator();ImGui.PopID();}if(vault.Personas.Count<40&&ImGui.Button("Add name")){vault.Personas.Add(new());Session.Save();}
  Style.Title("Device verification","Compare fingerprints using a separate trusted channel before approving. An approved device can receive conversation history.");
  foreach(var device in Session.State.Devices){ImGui.PushID(device.Id);ImGui.TextWrapped(device.Role+" · "+PrivateChatCrypto.Fingerprint(device));if(device.Id!=vault.Identity.Id&&ImGui.Button("Approve and share keys")){approve=device;ImGui.OpenPopup("Confirm device");}if(ImGui.BeginPopup("Confirm device")){ImGui.TextWrapped("Have you compared this fingerprint with the other device?");if(ImGui.Button("Approve verified device")&&approve is not null){Session.Approve(approve);ImGui.CloseCurrentPopup();}ImGui.EndPopup();}ImGui.PopID();}
  ImGui.InputText("Backup password",ref backupPassword,200,ImGuiInputTextFlags.Password);EditingText|=ImGui.IsItemActive();ImGui.BeginDisabled(backupPassword.Length<8);if(ImGui.Button("Export encrypted backup")){files.SaveFileDialog("Encrypted chat backup",".json","PetService-chat-backup",".json",(ok,path)=>{if(ok)try{File.WriteAllText(path,PrivateChatCrypto.Backup(vault,backupPassword));backupPassword="";}catch{Session.Error="Could not save backup.";}});}ImGui.EndDisabled();if(ImGui.Button("Restore encrypted backup"))files.OpenFileDialog("Restore chat backup",".json",(ok,path)=>{if(ok)try{var file=new FileInfo(path);if(file.Length>5000000)throw new InvalidOperationException();Session.Import(File.ReadAllText(path),backupPassword);backupPassword="";}catch{Session.Error="Could not open backup. Check its password and participant.";}});
  if(Session.Room.Length>0){var title=Session.TitleDraft;if(ImGui.InputText("Conversation title",ref title,120))Session.TitleDraft=title;EditingText|=ImGui.IsItemActive();if(ImGui.Button("Rename selected conversation"))Session.Rename();}
  Style.Title("Delete conversation","Either participant can delete one conversation for both people. Hold Ctrl+Shift and click, then confirm. Copied text and backups cannot be recalled.");Danger("Delete selected conversation for both","delete");
  if(master){Danger("Clear selected history","clear");Danger("Clear ALL conversations for "+pet,"all");}
  if(confirm.Length>0){ImGui.TextWrapped("Confirm "+confirm+" for "+(master?pet:plugin.Configuration.PetName)+"? This affects both participants.");if(ImGui.Button("Confirm permanent deletion")){Session.Clear(confirm=="delete",confirm=="all");confirm="";}ImGui.SameLine();if(ImGui.Button("Cancel"))confirm="";}
  if(ImGui.Button(master?"Use pet chat":"Use Master chat")){if(master)OpenPet();else{Session.Lock();master=true;}}
 }
 private void Danger(string label,string action){ImGui.BeginDisabled(Session.Busy||action!="all"&&Session.Room.Length==0);if(ImGui.Button(label)&&ImGui.GetIO().KeyCtrl&&ImGui.GetIO().KeyShift)confirm=action;ImGui.EndDisabled();}
 private void Audio(string op,object data,Action<JsonElement>? done=null){if(audioTask is not null)return;audioTask=Session.AudioRequest(op,data);audioDone=done;}
 private void DrawSounds()
 {
  Style.Title("Master sounds","Upload your own PCM WAV clips, up to 15 seconds and 2 MB. The library and event assignments start empty.");if(ImGui.Button("Refresh sound library"))Audio("sounds",new{},v=>catalog=JsonSerializer.Deserialize<SoundCatalog>(v,ServiceClient.Json)!);
  var sync=catalog.Cache;var current=sync is not null&&catalog.Library is not null&&sync.Scope==catalog.Library.Scope&&sync.Revision==catalog.Library.Revision;
  Style.Title("Pet sound downloads",sync is null?"Waiting for the pet's first sound-library report.":$"{sync.Cached} / {sync.Total} clips ready · {sync.PendingDeletes} file removals pending");
  ImGui.TextWrapped(current&&sync!.Complete?"The pet has the current library. New uploads download automatically; removals synchronize on reconnect.":"Waiting for the pet to finish synchronizing the current library. Refresh to check progress.");
  if(sync?.ReportedAtUtc is { } at)ImGui.TextColored(Style.Muted,"Last report: "+LocalState.Parse(at).ToLocalTime().ToString("g"));
  if(ImGui.Button("Choose WAV…"))files.OpenFileDialog("Upload sound",".wav",(ok,path)=>{if(ok)try{var f=new FileInfo(path);if(f.Length>2000000)throw new InvalidOperationException();upload=File.ReadAllBytes(path);soundName=Path.GetFileNameWithoutExtension(path);}catch{Session.Error="Choose a PCM WAV file up to 2 MB.";}});ImGui.InputText("Sound name",ref soundName,80);EditingText|=ImGui.IsItemActive();if(upload is not null&&ImGui.Button(replaceSound.Length>0?"Replace sound":"Upload sound")){Audio("uploadSound",new{id=replaceSound.Length>0?replaceSound:Guid.NewGuid().ToString(),name=soundName,audio=Convert.ToBase64String(upload)},_=>{upload=null;replaceSound="";reloadSounds=true;});}
  foreach(var sound in catalog.Sounds){ImGui.PushID(sound.Id);ImGui.TextWrapped(sound.Name+" · "+sound.Seconds.ToString("0.00")+" seconds");if(ImGui.Button("Preview here")&&previewTask is null)previewTask=Session.SoundBytes(sound.Id);ImGui.SameLine();if(ImGui.Button("Play for pet"))Audio("playSound",new{id=Guid.NewGuid().ToString(),soundId=sound.Id});ImGui.SameLine();if(ImGui.Button("Replace file")){replaceSound=sound.Id;soundName=sound.Name;}ImGui.SameLine();if(ImGui.Button("Remove sound"))Audio("deleteSound",new{id=sound.Id},_=>{catalog.Sounds.Remove(sound);reloadSounds=true;});ImGui.PopID();}
  Style.Title("Recent playback requests","Started and ended describe playback state, not proof the sound was heard.");foreach(var request in catalog.Recent)ImGui.TextWrapped((catalog.Sounds.FirstOrDefault(s=>s.Id==request.SoundId)?.Name??"Removed sound")+" · "+request.Event+" · "+request.State);
  Style.Title("Optional event sounds","Every assignment starts at None. Reward Points play once per positive award when assigned.");foreach(var eventName in catalog.Events){ImGui.PushID(eventName);var rule=catalog.Rules.FirstOrDefault(r=>r.Event==eventName);if(rule is null){rule=new(){Event=eventName,Threshold=eventName=="taskDueSoon"?15:100};catalog.Rules.Add(rule);}if(Style.BeginCombo(System.Text.RegularExpressions.Regex.Replace(eventName,"([A-Z])"," $1"),catalog.Sounds.FirstOrDefault(s=>s.Id==rule.SoundId)?.Name??"None")){if(ImGui.Selectable("None",rule.SoundId is null))rule.SoundId=null;foreach(var s in catalog.Sounds)if(ImGui.Selectable(s.Name,s.Id==rule.SoundId))rule.SoundId=s.Id;ImGui.EndCombo();}var cooldown=rule.Cooldown;if(Style.InputInt("Cooldown seconds",ref cooldown))rule.Cooldown=Math.Clamp(cooldown,0,3600);if(eventName is "pointMilestone" or "playtimeMilestone" or "taskDueSoon"){var threshold=rule.Threshold;if(Style.InputInt(eventName=="pointMilestone"?"Every reward points":eventName=="playtimeMilestone"?"Every observed playtime minutes":"Minutes before due",ref threshold))rule.Threshold=Math.Clamp(threshold,1,1000000);}else if(eventName is "zoneArrival" or "itemAcquired" or "dutyCompleted" or "retainerObserved"){var filter=rule.Filter;if(ImGui.InputText("Optional location / item filter",ref filter,100))rule.Filter=filter;}EditingText|=ImGui.IsItemActive();if(ImGui.Button("Save assignment"))Audio("soundRule",rule);ImGui.Separator();ImGui.PopID();}
 }
 public override void OnClose(){EditingText=false;try{Session.StoreDraft();var size=lastSize;plugin.Mutate(c=>{c.ChatWindow.Width=size.X;c.ChatWindow.Height=size.Y;});}catch{}}
 public void Dispose(){try{preview.Dispose();}finally{try{Session.Dispose();}finally{try{files.Reset();}finally{password=backupPassword="";}}}}
}
