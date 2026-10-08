using System.Collections;
using Dalamud.Interface.Textures;
using Lumina.Data.Files;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PetService.Core;
namespace PetService;
internal sealed class IntegrationBridge(Plugin plugin):IDisposable
{
 private readonly List<IconAsset> icons=[];private readonly HashSet<uint> attemptedIcons=[];
 internal List<IconAsset> Icons=>icons.ToList();
 internal void AcceptedIcons(List<uint> ids)=>icons.RemoveAll(x=>ids.Contains(x.IconId));
 internal void RejectedIcons(List<uint> ids){AcceptedIcons(ids);attemptedIcons.ExceptWith(ids);}
 private void CaptureIcons(){if(icons.Count>=8)return;foreach(var id in plugin.Configuration.Features.MissingIconIds.Where(x=>!attemptedIcons.Contains(x)).Take(Math.Min(2,8-icons.Count))){attemptedIcons.Add(id);try{var lookup=new GameIconLookup(id);var path=Plugin.Textures.GetIconPath(lookup);var file=Plugin.Data.GetFile<TexFile>(path);if(file is null)continue;var bytes=PngIcon.EncodeBgra(file.Header.Width,file.Header.Height,file.ImageData);icons.Add(new(){IconId=id,Png=Convert.ToBase64String(bytes)});plugin.RequestObservationUpload();}catch{}}}
 private long nextCatalog;private string catalogFingerprint="";
 internal object? Catalog{get;private set;}
 internal void AcceptedCatalog(object? sent){if(ReferenceEquals(sent,Catalog))Catalog=null;}
 internal void RequestCatalog(){nextCatalog=0;catalogFingerprint="";}
 private string boundCharacter="";private long nextCleanup;private bool migrated;
 private List<IntegrationLease> Leases()=>IntegrationLifetime.ForCharacter(plugin.Configuration.IntegrationLeases,plugin.CurrentCharacterId);
 private string GetTitle()=>Plugin.PluginInterface.GetIpcSubscriber<string>("Honorific.GetLocalCharacterTitle").InvokeFunc();
 private IEnumerable<object> Tuples(string route)=>((IEnumerable)Plugin.PluginInterface.GetIpcSubscriber<object>("Moodles."+route).InvokeFunc()).Cast<object>();
 private static object? At(object value,int index)=>value is ITuple t && index<t.Length?t[index]:null;
 private static Guid Id(object value,int index=1)=>At(value,index) is Guid g?g:Guid.Empty;
 private static bool Removable(object value)=>At(value,15) is false && string.IsNullOrEmpty(At(value,14)?.ToString()) && Id(value,11)==Guid.Empty;
 private static object PatchTuple(object tuple,int index,object value){var type=tuple.GetType();if(index<7){type.GetField("Item"+(index+1))!.SetValue(tuple,value);}else{var rest=type.GetField("Rest")!;rest.SetValue(tuple,PatchTuple(rest.GetValue(tuple)!,index-7,value));}return tuple;}
 private IEnumerable<object> Active()=>Tuples("GetClientStatusManagerInfoV2");
 internal void Update(){
  MigrateLeases();if(!migrated)return;
  var character=plugin.CurrentCharacterId;
  if(character!=boundCharacter){RequestRelease(boundCharacter);boundCharacter=character;ResetCatalog();nextCleanup=0;}
  if(character.Length==0 || !plugin.Ready){Catalog=null;return;}
  var paused=!plugin.SharingAllowed || plugin.LocalRelease;
  if(paused)RequestRelease(character);
  if(Environment.TickCount64>=nextCleanup){if(plugin.Configuration.IntegrationCleanupCharacters.Contains(character)||Leases().Any(x=>x.Pending||x.PetId!=plugin.Configuration.PetId))Cleanup();else Expire();ScheduleCleanup();}
  if(paused){CancelQueued(character);Catalog=null;return;}
  CaptureIcons();
  foreach(var cmd in plugin.Configuration.Features.IntegrationCommands.Where(c=>c.State=="queued" && c.CharacterId==plugin.CurrentCharacterId).OrderBy(c=>c.Order).ThenBy(c=>c.RequestedAtUtc,StringComparer.Ordinal).ThenBy(c=>c.Id,StringComparer.Ordinal)) {
   if(!IntegrationLifetime.CanApply(plugin.SharingAllowed,plugin.Ready,plugin.LocalRelease,plugin.Configuration.IntegrationCleanupCharacters.Contains(character)))break;
   if(plugin.Configuration.FeatureResults.Any(r=>r.Id==cmd.Id)||plugin.Configuration.IntegrationLeases.Any(x=>x.Id==cmd.Id))continue;
   if(cmd.ExpiresAtUtc is not null && LocalState.Parse(cmd.ExpiresAtUtc)<=plugin.Now){Result(cmd.Id,"expired","");continue;}
   try {if(cmd.Kind=="honorific")Honorific(cmd);else if(cmd.Kind=="moodles")Moodles(cmd);else Result(cmd.Id,"unavailable","Unsupported integration.");}catch{if(Leases().Any(x=>x.Id==cmd.Id&&x.Pending))Cleanup();Result(cmd.Id,"unavailable","Compatible plugin or remote apply permission unavailable.");}
   nextCatalog=0;nextCleanup=0;
  }
  if(Environment.TickCount64<nextCatalog)return;nextCatalog=Environment.TickCount64+15000;
  object honorific=new{available=false},moodles=new{available=false};
  try {var v=Plugin.PluginInterface.GetIpcSubscriber<(uint,uint)>("Honorific.ApiVersion").InvokeFunc();if(v.Item1!=3)throw new InvalidOperationException();var raw=Plugin.PluginInterface.GetIpcSubscriber<string,uint,object>("Honorific.GetCharacterTitleList").InvokeFunc(Plugin.Player.CharacterName,Plugin.Player.HomeWorld.RowId);var json=Newtonsoft.Json.Linq.JArray.FromObject(raw);honorific=new{available=true,version=$"{v.Item1}.{v.Item2}",current=SanitizeTitle(GetTitle()),titles=json.Select(x=>new{title=x["Title"]?.ToString()??"",prefix=x["IsPrefix"]?.Value<bool>()??false,colour=Hex(x["Color"]),glow=Hex(x["Glow"])}).ToList()};}catch{}
  try {var v=Plugin.PluginInterface.GetIpcSubscriber<int>("Moodles.Version").InvokeFunc();if(v!=4)throw new InvalidOperationException();var definitions=Tuples("GetStatusInfoListV2").ToList();var statuses=Plugin.PluginInterface.GetIpcSubscriber<List<(Guid,uint,string,string)>>("Moodles.GetRegisteredMoodlesV2").InvokeFunc();var presets=Tuples("GetPresetsInfoListV2").Select(x=>new{id=Id(x,0),title=At(x,3)?.ToString()??"",statuses=((IEnumerable?)At(x,1))?.Cast<Guid>().ToList()??[]}).ToList();moodles=new{available=true,version=v,statuses=statuses.Select(x=>new{id=x.Item1,iconId=x.Item2,path=x.Item3,title=x.Item4,description=definitions.FirstOrDefault(d=>Id(d)==x.Item1) is { } d?(At(d,4)?.ToString()??""):"",removable=definitions.Any(d=>Id(d)==x.Item1&&Removable(d))}).ToList(),presets,current=Active().Select(x=>new{id=Id(x),title=At(x,3)?.ToString()??"",iconId=At(x,2),description=At(x,4)?.ToString()??"",stacks=At(x,8),removable=Removable(x)}).ToList()};}catch{}
  var catalogValue=new{characterId=plugin.CurrentCharacterId,honorific,moodles};var hash=System.Text.Json.JsonSerializer.Serialize(catalogValue,ServiceClient.Json);if(hash!=catalogFingerprint){catalogFingerprint=hash;Catalog=catalogValue;plugin.RequestObservationUpload();}
 }
 private static string Hex(JToken? token){if(token is not JObject o)return "";try{return $"#{(int)Math.Clamp((o["X"]?.Value<float>()??1)*255,0,255):X2}{(int)Math.Clamp((o["Y"]?.Value<float>()??1)*255,0,255):X2}{(int)Math.Clamp((o["Z"]?.Value<float>()??1)*255,0,255):X2}";}catch{return "";}}
 private static object SanitizeTitle(string raw){try{var x=Newtonsoft.Json.Linq.JObject.Parse(raw);return new{title=x["Title"]?.ToString()??"",prefix=x["IsPrefix"]?.Value<bool>()??false,colour=Hex(x["Color"]),glow=Hex(x["Glow"])};}catch{return new{title=""};}}
 private static object? Colour(string value){if(value.Length!=7 || value[0]!='#' || !uint.TryParse(value.AsSpan(1),System.Globalization.NumberStyles.HexNumber,null,out var rgb))return null;return new{X=((rgb>>16)&255)/255f,Y=((rgb>>8)&255)/255f,Z=(rgb&255)/255f};}
 private void Honorific(IntegrationCommand cmd){
  var version=Plugin.PluginInterface.GetIpcSubscriber<(uint,uint)>("Honorific.ApiVersion").InvokeFunc();if(version.Item1!=3)throw new InvalidOperationException();
  if(cmd.Operation=="clear"){ReleaseTitle();var remaining=TitleRemaining(plugin.CurrentCharacterId);if(!remaining)foreach(var lease in Leases().Where(x=>x.Kind=="honorific")){Report(lease,"released","Cleared by a PetService command.");Forget(lease);}Result(cmd.Id,remaining?"failed":"released",remaining?"Honorific did not confirm release.":"");return;}
  var previous=GetTitle();var json=JsonConvert.SerializeObject(new{Title=cmd.Title,IsPrefix=cmd.Prefix,IsOriginal=false,Color=Colour(cmd.Colour),Glow=Colour(cmd.Glow)});
  if(!Prepare(cmd,[],json,previous))return;
  Plugin.PluginInterface.GetIpcSubscriber<int,string,object>("Honorific.SetCharacterTitle").InvokeAction(0,json);
  var read=GetTitle();
  if(!TitleMatches(read,json)){Cleanup();Result(cmd.Id,"failed","Honorific did not confirm the requested title, placement and colours.");return;}
  var replaced=Leases().Where(x=>x.Kind=="honorific"&&x.Id!=cmd.Id).ToList();
  if(!plugin.Mutate(c=>{c.ManagedTitles[cmd.CharacterId]=read;var lease=c.IntegrationLeases.Single(x=>x.Id==cmd.Id);lease.Pending=false;lease.TitleJson=read;c.IntegrationLeases.RemoveAll(x=>x.CharacterId==cmd.CharacterId&&x.Kind=="honorific"&&x.Id!=cmd.Id);})){Cleanup();Result(cmd.Id,"failed","Could not save the applied title; release was requested.");return;}
  foreach(var old in replaced)Report(old,"released","Replaced by another PetService title.");
  Result(cmd.Id,"applied","");
 }
 private void Moodles(IntegrationCommand cmd){
  if(Plugin.PluginInterface.GetIpcSubscriber<int>("Moodles.Version").InvokeFunc()!=4)throw new InvalidOperationException();
  var local=Plugin.Objects.LocalPlayer!;var ids=new List<Guid>();
  if(cmd.Operation=="clear"){ids=Managed().ToList();Remove(ids);ReleaseRemovedCommands();var remaining=Managed().Intersect(ids).Any();Result(cmd.Id,remaining?"failed":"released",remaining?"Moodles did not confirm removal of all managed statuses.":"All PetService-managed statuses removed.");return;}
  if(cmd.Operation=="preset"){var preset=Tuples("GetPresetsInfoListV2").First(x=>Id(x,0).ToString()==cmd.Preset);ids=((IEnumerable)At(preset,1)!).Cast<Guid>().ToList();}
  else ids.Add(Guid.Parse(cmd.StatusId));
  if(cmd.Operation=="remove"){Remove(ids);ReleaseRemovedCommands();var remaining=Managed().Intersect(ids).Any();Result(cmd.Id,remaining?"failed":"released",remaining?"Moodles did not confirm removal.":"");return;}
  var definitions=Tuples("GetStatusInfoListV2").ToDictionary(x=>Id(x));var existing=Active().Select(x=>Id(x)).ToHashSet();
  if(ids.Any(id=>!definitions.TryGetValue(id,out var d)||!Removable(d))){Result(cmd.Id,"failed","This selection contains a persistent, restricted, or chained status.");return;}
  if(ids.Any(id=>existing.Contains(id)&&!Managed().Contains(id))){Result(cmd.Id,"failed","An existing status is owned outside PetService.");return;}
  ids=ids.Distinct().ToList();var previouslyManaged=Managed().ToHashSet();if(!Prepare(cmd,ids))return;
  var applyFailed=false;foreach(var id in ids)try{if(cmd.DurationMinutes>0 || cmd.Stacks is not null){var data=definitions[id];if(cmd.DurationMinutes>0)data=PatchTuple(data,6,(long)cmd.DurationMinutes*60000);if(cmd.Stacks is { } stacks)data=PatchTuple(data,8,stacks);Plugin.PluginInterface.GetIpcSubscriber<object,IPlayerCharacter,object>("Moodles.AddOrUpdateMoodleByDataByPlayerV2").InvokeAction(data,local);}else Plugin.PluginInterface.GetIpcSubscriber<Guid,IPlayerCharacter,object>("Moodles.AddOrUpdateMoodleByPlayerV2").InvokeAction(id,local);}catch{applyFailed=true;break;}
  var applied=Active().Select(x=>Id(x)).ToHashSet();var observed=ids.Where(applied.Contains).ToList();
  var replaced=Leases().Where(x=>x.Kind=="moodles"&&x.Id!=cmd.Id&&x.StatusIds.Count>0&&x.StatusIds.All(observed.Contains)).ToList();
  if(!plugin.Mutate(c=>{
   c.ManagedMoodles[cmd.CharacterId].RemoveAll(x=>ids.Contains(x)&&!observed.Contains(x)&&!previouslyManaged.Contains(x));
   foreach(var old in c.IntegrationLeases.Where(x=>x.CharacterId==cmd.CharacterId&&x.Kind=="moodles"&&x.Id!=cmd.Id))old.StatusIds=old.StatusIds.Except(observed).ToList();
   var lease=c.IntegrationLeases.Single(x=>x.Id==cmd.Id);lease.StatusIds=observed;lease.Pending=false;
   c.IntegrationLeases.RemoveAll(x=>x.CharacterId==cmd.CharacterId&&x.Kind=="moodles"&&x.StatusIds.Count==0);
  })){Cleanup();Result(cmd.Id,"failed","Could not save the applied statuses; release was requested.");return;}
  foreach(var old in replaced)Report(old,"released","Replaced by a newer PetService status request.");
  if(observed.Count==ids.Count&&!applyFailed){Result(cmd.Id,"applied","");}else Result(cmd.Id,"failed","Moodles did not confirm all statuses. Check its remote apply permissions.");
 }
 private bool Prepare(IntegrationCommand cmd,List<Guid> ids,string title="",string previous="")
 {
  if(plugin.Mutate(c=>{
   var lease=Lease(cmd);lease.Pending=true;lease.TitleJson=title;lease.StatusIds=ids.ToList();c.IntegrationLeases.Add(lease);
   if(cmd.Kind=="honorific"){if(!c.ManagedTitles.ContainsKey(cmd.CharacterId))c.PreviousTitles[cmd.CharacterId]=previous;}
   else {if(!c.ManagedMoodles.TryGetValue(cmd.CharacterId,out var own))c.ManagedMoodles[cmd.CharacterId]=own=[];foreach(var id in ids)if(!own.Contains(id))own.Add(id);}
  }))return true;
  Result(cmd.Id,"failed","Local state could not be saved; no new effect was applied.");return false;
 }
 private static bool TitleMatches(string actual,string expected)
 {
  if(actual==expected)return true;
  try {
   var a=JObject.Parse(actual);var b=JObject.Parse(expected);
   if((a["Title"]?.Value<string>()??"")!=(b["Title"]?.Value<string>()??""))return false;
   foreach(var key in new[]{"IsPrefix","IsOriginal"})if((a[key]?.Value<bool>()??false)!=(b[key]?.Value<bool>()??false))return false;
   foreach(var key in new[]{"Color","Glow","Color3"}){
    var av=a[key] as JObject;var bv=b[key] as JObject;if((av is null)!=(bv is null))return false;
    if(av is not null&&bv is not null)foreach(var axis in new[]{"X","Y","Z"})if(Math.Abs((av[axis]?.Value<double>()??0)-(bv[axis]?.Value<double>()??0))>0.000001)return false;
   }
   foreach(var key in new[]{"GradientColourSet","GradientAnimationStyle"})if((a[key]?.Value<int?>())!=(b[key]?.Value<int?>()))return false;
   return true;
  }catch{return false;}
 }
 private void ReleaseRemovedCommands(){foreach(var lease in Leases().Where(x=>x.Kind=="moodles"))if(!Managed().Intersect(lease.StatusIds).Any()){Report(lease,"released","Removed by another PetService command.");Forget(lease);}}
 private IntegrationLease Lease(IntegrationCommand command)=>new(){Id=command.Id,PetId=plugin.Configuration.PetId,CharacterId=command.CharacterId,Kind=command.Kind,ExpiresAtUtc=command.ExpiresAtUtc};
 private void Forget(IntegrationLease lease)=>plugin.Mutate(c=>c.IntegrationLeases.RemoveAll(x=>x.Id==lease.Id&&x.CharacterId==lease.CharacterId));
 private void Report(IntegrationLease lease,string state,string detail){if(lease.PetId==plugin.Configuration.PetId)Result(lease.Id,state,detail);}
 private void ResetCatalog(){Catalog=null;catalogFingerprint="";nextCatalog=0;icons.Clear();attemptedIcons.Clear();}
 private void MigrateLeases(){if(migrated)return;var saved=plugin.Mutate(c=>{foreach(var cmd in c.Features.IntegrationCommands.Where(x=>x.State=="applied"&&!c.IntegrationLeases.Any(l=>l.Id==x.Id)).OrderByDescending(x=>x.Order).ThenByDescending(x=>x.RequestedAtUtc,StringComparer.Ordinal)){var lease=Lease(cmd);if(cmd.Kind=="honorific"&&c.ManagedTitles.TryGetValue(cmd.CharacterId,out var title)&&!c.IntegrationLeases.Any(x=>x.CharacterId==cmd.CharacterId&&x.Kind=="honorific")){try{if(JObject.Parse(title)["Title"]?.ToString()==cmd.Title)c.IntegrationLeases.Add(lease);}catch{}}else if(cmd.Kind=="moodles"&&c.ManagedMoodles.TryGetValue(cmd.CharacterId,out var managed)){var claimed=c.IntegrationLeases.Where(x=>x.CharacterId==cmd.CharacterId&&x.Kind=="moodles").SelectMany(x=>x.StatusIds).ToHashSet();lease.StatusIds=cmd.Operation=="preset"?managed.Where(x=>!claimed.Contains(x)).ToList():Guid.TryParse(cmd.StatusId,out var id)&&managed.Contains(id)&&!claimed.Contains(id)?[id]:[];if(lease.StatusIds.Count>0)c.IntegrationLeases.Add(lease);}}foreach(var id in c.ManagedTitles.Keys.Concat(c.ManagedMoodles.Where(x=>x.Value.Count>0).Select(x=>x.Key)).Distinct())if(!c.IntegrationLeases.Any(x=>x.CharacterId==id)&&!c.IntegrationCleanupCharacters.Contains(id))c.IntegrationCleanupCharacters.Add(id);});migrated=saved;}
 private void ScheduleCleanup(){var remaining=Leases().Where(x=>x.ExpiresAtUtc is not null).Select(x=>(LocalState.Parse(x.ExpiresAtUtc!)-plugin.Now).TotalMilliseconds).Where(x=>x>0).DefaultIfEmpty(15000).Min();nextCleanup=Environment.TickCount64+(long)Math.Clamp(remaining,1,15000);}
 private void CancelQueued(string character){if(character.Length==0)return;var commands=plugin.Configuration.Features.IntegrationCommands.Where(x=>x.State=="queued"&&x.CharacterId==character).ToList();if(commands.Count==0)return;plugin.Mutate(c=>{foreach(var cmd in c.Features.IntegrationCommands.Where(x=>x.State=="queued"&&x.CharacterId==character)){cmd.State="released";c.FeatureResults.Add(new(){Id=cmd.Id,State="released",AtUtc=LocalState.Stamp(plugin.Now),Detail="Cancelled by a local pause or character change before application."});}});plugin.RequestObservationUpload();}
 private void RequestRelease(string character){if(character.Length==0)return;CancelQueued(character);var config=plugin.Configuration;if(!config.IntegrationCleanupCharacters.Contains(character)&&(config.ManagedTitles.ContainsKey(character)||(config.ManagedMoodles.GetValueOrDefault(character)?.Count??0)>0||config.IntegrationLeases.Any(x=>x.CharacterId==character)))plugin.Mutate(c=>c.IntegrationCleanupCharacters.Add(character));}
 private void Cleanup(){var character=plugin.CurrentCharacterId;ReleaseTitle();Remove(Managed().ToList());foreach(var lease in Leases()){var remaining=lease.Kind=="honorific"?TitleRemaining(character):Managed().Intersect(lease.StatusIds).Any();Report(lease,remaining?"failed":"released",remaining?"Compatible plugin did not confirm release during local pause.":"Released by a local pause or character change.");if(!remaining)Forget(lease);}if(!plugin.Configuration.ManagedTitles.ContainsKey(character)&&Managed().Count==0&&Leases().Count==0)plugin.Mutate(c=>c.IntegrationCleanupCharacters.Remove(character));}
 private void Expire(){foreach(var lease in Leases().Where(x=>IntegrationLifetime.Expired(x,plugin.Now))){if(lease.Kind=="honorific")ReleaseTitle();else Remove(lease.StatusIds);var remaining=lease.Kind=="honorific"?TitleRemaining(plugin.CurrentCharacterId):Managed().Intersect(lease.StatusIds).Any();Report(lease,remaining?"failed":"expired",remaining?"Compatible plugin did not confirm removal at expiry.":"");if(!remaining)Forget(lease);}}
 internal void CharacterChanged(){RequestRelease(boundCharacter);ResetCatalog();nextCleanup=0;}
 private List<Guid> Managed()=>plugin.Configuration.ManagedMoodles.GetValueOrDefault(plugin.CurrentCharacterId)??[];
 private void Remove(List<Guid> ids){if(Plugin.Objects.LocalPlayer is not { } local)return;var own=Managed();foreach(var id in ids.Where(own.Contains).ToList())try{Plugin.PluginInterface.GetIpcSubscriber<Guid,IPlayerCharacter,object>("Moodles.RemoveMoodleByPlayerV2").InvokeAction(id,local);if(!Active().Any(x=>Id(x)==id))plugin.Mutate(c=>c.ManagedMoodles[plugin.CurrentCharacterId].Remove(id));}catch{}}
 private void ReleaseTitle()
 {
  var id=plugin.CurrentCharacterId;var own=plugin.Configuration.ManagedTitles.GetValueOrDefault(id);
  var candidates=Leases().Where(x=>x.Kind=="honorific"&&x.TitleJson.Length>0).Select(x=>x.TitleJson).ToList();if(own is not null)candidates.Add(own);
  if(candidates.Count==0)return;
  try{
   var current=GetTitle();if(candidates.Any(x=>TitleMatches(current,x))){
    Plugin.PluginInterface.GetIpcSubscriber<int,object>("Honorific.ClearCharacterTitle").InvokeAction(0);
    if(TitleMatches(GetTitle(),current)&&!TitleMatches(plugin.Configuration.PreviousTitles.GetValueOrDefault(id)??"",current))return;
   }
   plugin.Mutate(c=>{c.ManagedTitles.Remove(id);c.PreviousTitles.Remove(id);foreach(var lease in c.IntegrationLeases.Where(x=>x.CharacterId==id&&x.Kind=="honorific"))lease.TitleJson="";});
  }catch{}
 }
 private bool TitleRemaining(string character)=>plugin.Configuration.ManagedTitles.ContainsKey(character)||Leases().Any(x=>x.Kind=="honorific"&&x.TitleJson.Length>0);
 internal void Release(){RequestRelease(boundCharacter);RequestRelease(plugin.CurrentCharacterId);if(plugin.CurrentCharacterId.Length>0&&plugin.Ready&&plugin.Configuration.IntegrationCleanupCharacters.Contains(plugin.CurrentCharacterId))Cleanup();ResetCatalog();nextCleanup=0;}
 private void Result(string id,string state,string detail){plugin.Mutate(c=>{c.FeatureResults.RemoveAll(r=>r.Id==id&&r.State==state);c.FeatureResults.Add(new(){Id=id,AtUtc=LocalState.Stamp(plugin.Now),State=state,Detail=detail});});plugin.RequestObservationUpload();}
 public void Dispose()=>Release();
}
