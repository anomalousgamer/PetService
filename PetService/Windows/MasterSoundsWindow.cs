using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.ImGuiFileDialog;
using PetService.Core;
namespace PetService.Windows;

internal sealed class MasterSoundsWindow:Window,IDisposable
{
 private readonly Plugin plugin;
 private readonly MasterPortalClient client=new();
 private readonly FileDialogManager files=new();
 private readonly CancellationTokenSource cancel=new();
 private string password="",pet="",soundName="",replaceSound="",error="";
 private SoundCatalog catalog=new();
 private Task<JsonElement>? audioTask;private Action<JsonElement>? audioDone;
 private Task<byte[]>? previewTask;private LocalSoundPlayer? preview;private byte[]? upload;
 private bool reloadSounds,disposed;private long nextErrorLog;
 internal bool EditingText{get;private set;}
 internal MasterSoundsWindow(Plugin plugin):base("Master sounds###PetServiceMasterSounds")
 {
  this.plugin=plugin;SizeConstraints=new(){MinimumSize=new(650,450),MaximumSize=new(float.MaxValue)};
  Size=new(820,680);SizeCondition=ImGuiCond.FirstUseEver;
 }
 internal void Open(string name,string secret){ResetWork();pet=name;password=secret;reloadSounds=true;IsOpen=true;}
 private void Failure(Exception problem,string message){error=message;if(Environment.TickCount64<nextErrorLog)return;nextErrorLog=Environment.TickCount64+30000;try{Plugin.Log.Warning(problem,"Pet Service Master sound window failed.");}catch{}}
 private void StopPreview(){var old=preview;preview=null;if(old is null)return;try{old.Dispose();}catch(Exception problem){Failure(problem,"Could not stop the sound preview.");}}
 private void ResetWork()
 {
  if(audioTask is not null)_=audioTask.ContinueWith(t=>{_=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
  if(previewTask is not null)_=previewTask.ContinueWith(t=>{_=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
  audioTask=null;audioDone=null;previewTask=null;reloadSounds=false;StopPreview();catalog=new();upload=null;replaceSound="";error="";
 }
 public override void PreDraw(){Style.ApplyTheme(plugin.Configuration.Features.Identity.Theme);Style.Push();}
 public override void PostDraw()=>Style.Pop();
 public override void Draw()
 {
  EditingText=false;
  try{
   if(password.Length==0){ImGui.TextWrapped("Open Master sounds from your unlocked Master portal.");return;}
   Style.Title("MASTER SOUNDS",pet);
   if(ImGui.Button("Lock sound library")){ResetWork();password="";return;}
   files.Draw();
   if(audioTask is {IsCompleted:true}){var task=audioTask;var complete=audioDone;audioTask=null;audioDone=null;try{complete?.Invoke(task.GetAwaiter().GetResult());}catch(Exception problem){Failure(problem,"Could not update the sound library. "+problem.Message);}}
   if(previewTask is {IsCompleted:true}){var task=previewTask;previewTask=null;try{preview??=new();preview.Play(task.GetAwaiter().GetResult(),.5f);}catch(Exception problem){StopPreview();Failure(problem,"Could not preview that sound.");}}
   if(reloadSounds&&audioTask is null){reloadSounds=false;Audio("sounds",new{},v=>catalog=JsonSerializer.Deserialize<SoundCatalog>(v,ServiceClient.Json)!);}
   DrawSounds();
   if(error.Length>0)ImGui.TextWrapped(error);
  }catch(Exception problem){Failure(problem,"The sound library could not complete this action. "+problem.Message);ImGui.TextWrapped(error);}
 }
 private void Audio(string op,object data,Action<JsonElement>? done=null){if(audioTask is not null||password.Length==0)return;error="";audioTask=client.Comms<JsonElement>(password,pet,op,data,cancel.Token);audioDone=done;}
 private void DrawSounds()
 {
  Style.Title("Master sounds","Upload your own PCM WAV clips, up to 15 seconds and 2 MB. The library and event assignments start empty.");if(ImGui.Button("Refresh sound library"))Audio("sounds",new{},v=>catalog=JsonSerializer.Deserialize<SoundCatalog>(v,ServiceClient.Json)!);
  var sync=catalog.Cache;var current=sync is not null&&catalog.Library is not null&&sync.Scope==catalog.Library.Scope&&sync.Revision==catalog.Library.Revision;
  Style.Title("Pet sound downloads",sync is null?"Waiting for the pet's first sound-library report.":$"{sync.Cached} / {sync.Total} clips ready · {sync.PendingDeletes} file removals pending");
  ImGui.TextWrapped(current&&sync!.Complete?"The pet has the current library. New uploads download automatically; removals synchronize on reconnect.":"Waiting for the pet to finish synchronizing the current library. Refresh to check progress.");
  if(sync?.ReportedAtUtc is { } at)ImGui.TextColored(Style.Muted,"Last report: "+LocalState.Parse(at).ToLocalTime().ToString("g"));
  if(ImGui.Button("Choose WAV…"))files.OpenFileDialog("Upload sound",".wav",(ok,path)=>{if(ok)try{var f=new FileInfo(path);if(f.Length>2000000)throw new InvalidOperationException();upload=File.ReadAllBytes(path);soundName=Path.GetFileNameWithoutExtension(path);}catch{error="Choose a PCM WAV file up to 2 MB.";}});ImGui.InputText("Sound name",ref soundName,80);EditingText|=ImGui.IsItemActive();if(upload is not null&&ImGui.Button(replaceSound.Length>0?"Replace sound":"Upload sound")){Audio("uploadSound",new{id=replaceSound.Length>0?replaceSound:Guid.NewGuid().ToString(),name=soundName,audio=Convert.ToBase64String(upload)},_=>{upload=null;replaceSound="";reloadSounds=true;});}
  foreach(var sound in catalog.Sounds){ImGui.PushID(sound.Id);ImGui.TextWrapped(sound.Name+" · "+sound.Seconds.ToString("0.00")+" seconds");if(ImGui.Button("Preview here")&&previewTask is null)previewTask=client.SoundBytes(password,sound.Id,cancel.Token);ImGui.SameLine();if(ImGui.Button("Play for pet"))Audio("playSound",new{id=Guid.NewGuid().ToString(),soundId=sound.Id});ImGui.SameLine();if(ImGui.Button("Replace file")){replaceSound=sound.Id;soundName=sound.Name;}ImGui.SameLine();if(ImGui.Button("Remove sound"))Audio("deleteSound",new{id=sound.Id},_=>{catalog.Sounds.Remove(sound);reloadSounds=true;});ImGui.PopID();}
  Style.Title("Recent playback requests","Started and ended describe playback state, not proof the sound was heard.");foreach(var request in catalog.Recent)ImGui.TextWrapped((catalog.Sounds.FirstOrDefault(s=>s.Id==request.SoundId)?.Name??"Removed sound")+" · "+request.Event+" · "+request.State);
  Style.Title("Optional event sounds","Every assignment starts at None. Reward Points play once per positive award when assigned.");foreach(var eventName in catalog.Events){ImGui.PushID(eventName);var rule=catalog.Rules.FirstOrDefault(r=>r.Event==eventName);if(rule is null){rule=new(){Event=eventName,Threshold=eventName=="taskDueSoon"?15:100};catalog.Rules.Add(rule);}if(Style.BeginCombo(System.Text.RegularExpressions.Regex.Replace(eventName,"([A-Z])"," $1"),catalog.Sounds.FirstOrDefault(s=>s.Id==rule.SoundId)?.Name??"None")){if(ImGui.Selectable("None",rule.SoundId is null))rule.SoundId=null;foreach(var s in catalog.Sounds)if(ImGui.Selectable(s.Name,s.Id==rule.SoundId))rule.SoundId=s.Id;ImGui.EndCombo();}var cooldown=rule.Cooldown;if(Style.InputInt("Cooldown seconds",ref cooldown))rule.Cooldown=Math.Clamp(cooldown,0,3600);if(eventName is "pointMilestone" or "playtimeMilestone" or "taskDueSoon"){var threshold=rule.Threshold;if(Style.InputInt(eventName=="pointMilestone"?"Every reward points":eventName=="playtimeMilestone"?"Every observed playtime minutes":"Minutes before due",ref threshold))rule.Threshold=Math.Clamp(threshold,1,1000000);}else if(eventName is "zoneArrival" or "itemAcquired" or "dutyCompleted" or "retainerObserved"){var filter=rule.Filter;if(ImGui.InputText("Optional location / item filter",ref filter,100))rule.Filter=filter;}EditingText|=ImGui.IsItemActive();if(ImGui.Button("Save assignment"))Audio("soundRule",rule);ImGui.Separator();ImGui.PopID();}
 }
 public override void OnClose(){EditingText=false;ResetWork();password="";}
 public void Dispose(){if(disposed)return;disposed=true;ResetWork();password="";try{cancel.Cancel();}finally{try{client.Dispose();}finally{try{files.Reset();}finally{cancel.Dispose();}}}}
}
