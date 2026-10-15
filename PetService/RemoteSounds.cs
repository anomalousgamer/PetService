using System.Security.Cryptography;
using System.Net.Http.Headers;
using System.Text.Json;
using NAudio.Wave;
namespace PetService;
public sealed class UploadedSound { public string Id{get;set;}=""; public string Name{get;set;}=""; public string Hash{get;set;}=""; public int Size{get;set;} public double Seconds{get;set;} }
public sealed class SoundRule { public string Event{get;set;}=""; public string? SoundId{get;set;} public int Cooldown{get;set;}=1; public string Filter{get;set;}=""; public int Threshold{get;set;}=100; }
public sealed class SoundCatalog { public List<UploadedSound> Sounds{get;set;}=[]; public List<SoundRule> Rules{get;set;}=[]; public List<string> Events{get;set;}=[]; public List<SoundRequest> Recent{get;set;}=[]; public Core.SoundLibraryManifest? Library{get;set;}public Core.SoundCacheReport? Cache{get;set;} }
public sealed class SoundRequest { public string Id{get;set;}=""; public string SoundId{get;set;}=""; public string ExpiresAtUtc{get;set;}=""; public string State{get;set;}="";public string Event{get;set;}="";public string CreatedAtUtc{get;set;}=""; }
public sealed class AudioPoll { public Core.SoundLibraryManifest? Library{get;set;}public string LibraryScope{get;set;}="";public long LibraryRevision{get;set;} public SoundRequest? Request{get;set;} public List<UploadedSound> Sounds{get;set;}=[]; public List<string> Preload{get;set;}=[]; }
public sealed class AudioResult { public string Id{get;set;}=""; public string State{get;set;}=""; }
public sealed class AudioEvent { public string Id{get;set;}=Guid.NewGuid().ToString(); public string Event{get;set;}=""; public string AtUtc{get;set;}=""; }
internal sealed class LocalSoundPlayer:IDisposable
{
 private WaveOutEvent? player; private WaveFileReader? reader; private MemoryStream? stream;
 internal bool Active=>player?.PlaybackState==PlaybackState.Playing;
 internal void Volume(float value){if(player is not null)player.Volume=Math.Clamp(float.IsFinite(value)?value:.5f,.1f,1);}
 internal void Play(byte[] bytes,float volume){Dispose();try{stream=new(bytes,false);reader=new(stream);if(reader.WaveFormat.Encoding!=WaveFormatEncoding.Pcm||reader.TotalTime.TotalSeconds>15||reader.TotalTime.TotalSeconds<=0)throw new InvalidOperationException("Choose a PCM WAV clip up to 15 seconds.");player=new();player.Init(reader);Volume(volume);player.Play();}catch{Dispose();throw;}}
 public void Dispose()
 {
  var oldPlayer=player;var oldReader=reader;var oldStream=stream;player=null;reader=null;stream=null;
  try{oldPlayer?.Stop();}catch{}
  try{oldPlayer?.Dispose();}finally{try{oldReader?.Dispose();}finally{oldStream?.Dispose();}}
 }
}
internal sealed class RemoteSounds:IDisposable
{
 private readonly Plugin plugin;private readonly HttpClient http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(12)};
 private readonly CancellationTokenSource cancel=new();private readonly LocalSoundPlayer player=new();private readonly Core.SoundLibraryCache cache;
 private Task<AudioPoll>? poll;private Task<byte[]>? download;private CancellationTokenSource? downloadCancel;
 private SoundRequest? downloading;private UploadedSound? downloadingSound;private string downloadingScope="";private int revision,pollRevision,downloadRevision;
 private List<AudioResult> submitted=[];private List<AudioEvent> submittedEvents=[];private long nextPoll,nextCleanup;
 private readonly Dictionary<string,long> retries=[];
 private string playing="",playingSound="",playingHash="",token="",lastGag="";private bool allowed,confirmed,disposed;
 internal string Status{get;private set;}="Waiting for sound library.";
 internal RemoteSounds(Plugin plugin){this.plugin=plugin;cache=new(Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(),"sound-cache"));}
 internal void Event(string kind){if(plugin.SharingAllowed&&plugin.Ready)plugin.Mutate(c=>{if(c.AudioEvents.Count<2000)c.AudioEvents.Add(new(){Event=kind,AtUtc=Core.LocalState.Stamp(plugin.Now)});});}
 internal void Stop(){var id=playing;playing=playingSound=playingHash="";try{player.Dispose();}finally{if(id.Length>0)Ack(id,"ended");}}
 private void Ack(string id,string state){if(id.Length>0)plugin.Mutate(c=>{if(!c.AudioResults.Any(r=>r.Id==id&&r.State==state))c.AudioResults.Add(new(){Id=id,State=state});});}
 internal bool CanPreview{get{try{return cache.Report()?.Cached>0;}catch{return false;}}}
 internal void Preview(){if(cache.Manifest is not { } manifest)return;foreach(var sound in manifest.Sounds!){var bytes=cache.Read(manifest.Scope,sound);if(bytes is null)continue;Stop();try{player.Play(bytes,plugin.Configuration.RemoteSoundVolume);playingSound=sound.Id;playingHash=sound.Hash;}catch{Stop();}break;}}
 private async Task<byte[]> Fetch(UploadedSound sound,string scope,string credential,CancellationToken cancellation)
 {
  var cached=cache.Read(scope,sound);if(cached is not null)return cached;
  using var request=new HttpRequestMessage(HttpMethod.Get,Core.ServiceClient.Endpoint(Core.ServiceClient.BaseUrl,"/api/device/sound?id="+sound.Id));request.Headers.Authorization=new("Bearer",credential);
  using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation);response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>2000000)throw new InvalidOperationException();
  var bytes=await response.Content.ReadAsByteArrayAsync(cancellation);if(bytes.Length!=sound.Size||bytes.Length>2000000||Convert.ToHexStringLower(SHA256.HashData(bytes))!=sound.Hash)throw new InvalidOperationException("Sound integrity check failed.");return bytes;
 }
 private void Download(UploadedSound sound,SoundRequest? request){downloading=request;downloadingSound=sound;downloadingScope=cache.Manifest!.Scope;downloadRevision=revision;downloadCancel=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);download=Fetch(sound,downloadingScope,token,downloadCancel.Token);}
 private void CancelDownload(){if(download is null)return;downloadCancel?.Cancel();downloadCancel?.Dispose();downloadCancel=null;_ = download.ContinueWith(t=>{_=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);if(downloading is not null)Ack(downloading.Id,"skipped");download=null;downloading=null;downloadingSound=null;}
 internal void Update()
 {
  if(disposed)return;
  var permit=plugin.SharingAllowed&&plugin.Ready;
  if(permit!=allowed||token!=plugin.Configuration.DeviceToken){revision++;allowed=permit;Stop();CancelDownload();confirmed=false;lastGag="";nextPoll=0;token=plugin.Configuration.DeviceToken;}
  if(Environment.TickCount64>=nextCleanup){nextCleanup=Environment.TickCount64+5000;try{cache.RetryCleanup();}catch{Status="Sound cleanup will retry.";}}
  // Process a library deletion before a download completion from the same frame.
  if(poll is {IsCompleted:true}){
   var task=poll;poll=null;
   try{var result=task.GetAwaiter().GetResult();plugin.Mutate(c=>{c.AudioResults.RemoveAll(r=>submitted.Any(x=>x.Id==r.Id&&x.State==r.State));c.AudioEvents.RemoveAll(e=>submittedEvents.Any(x=>x.Id==e.Id));});
    if(pollRevision==revision&&allowed){
     if(result.Library is not null)cache.Apply(result.Library);
     confirmed=cache.Manifest is { } manifest&&manifest.Scope==result.LibraryScope&&manifest.Revision==result.LibraryRevision;
     if(playingSound.Length>0&&!cache.Manifest!.Sounds!.Any(s=>s.Id==playingSound&&s.Hash==playingHash))Stop();
     if(downloadingSound is not null&&!cache.Desired(downloadingScope,downloadingSound))CancelDownload();
     if(result.Request is { } request){var sound=confirmed?cache.Manifest!.Sounds!.FirstOrDefault(s=>s.Id==request.SoundId):null;if(sound is null)Ack(request.Id,"skipped");else{CancelDownload();Download(sound,request);}}
     Status=confirmed?"Sound library connected.":"Waiting for current sound library.";
    }else if(result.Request is { } stale)Ack(stale.Id,"skipped");
   }catch{nextPoll=Environment.TickCount64+5000;Status="Sound synchronization will retry; downloaded files are kept.";}
  }
  if(download is {IsCompleted:true}){
   var task=download;var request=downloading;var sound=downloadingSound!;var scope=downloadingScope;download=null;downloading=null;downloadingSound=null;downloadCancel?.Dispose();downloadCancel=null;
   try{var bytes=task.GetAwaiter().GetResult();if(!allowed||revision!=downloadRevision||!cache.Store(scope,sound,bytes)){if(request is not null)Ack(request.Id,"skipped");}
    else if(request is not null){if(Core.LocalState.Parse(request.ExpiresAtUtc)<=plugin.Now)Ack(request.Id,"skipped");else if(!plugin.Configuration.SeenSoundRequests.Contains(request.Id)){
     if(!plugin.Mutate(c=>{c.SeenSoundRequests.Add(request.Id);if(c.SeenSoundRequests.Count>2000)c.SeenSoundRequests.RemoveRange(0,c.SeenSoundRequests.Count-2000);}))throw new InvalidOperationException();
     Stop();player.Play(bytes,plugin.Configuration.RemoteSoundVolume);playing=request.Id;playingSound=sound.Id;playingHash=sound.Hash;Ack(request.Id,"started");
    }}
   }catch{if(request is not null)Ack(request.Id,"failed");retries[sound.Id+sound.Hash]=Environment.TickCount64+30000;Status="A sound download will retry.";}
  }
  if(!allowed)return;
  player.Volume(plugin.Configuration.RemoteSoundVolume);if(playing.Length>0&&!player.Active)Stop();
  var gag=plugin.EffectiveSpeech.GarbleEnabled?"on":"off";if(lastGag.Length>0&&gag!=lastGag)Event(gag=="on"?"gagOn":"gagOff");lastGag=gag;
  if(download is null&&confirmed){try{var next=cache.NextDownload(s=>Environment.TickCount64>=retries.GetValueOrDefault(s.Id+s.Hash));if(next is not null)Download(next,null);}catch{Status="Sound cache access will retry.";}}
  var pending=plugin.CurrentPrompt;if(pending is {Kind:"reminder"}){var until=pending.Advanced?plugin.Configuration.Features.V6.Prompts.FirstOrDefault(p=>p.Id==pending.Id)?.SnoozedUntilUtc:pending.SnoozedUntilUtc;if(until is not null&&Core.LocalState.Parse(until)<=plugin.Now){var key=pending.Id+":"+until;if(!plugin.Configuration.SoundPromptReturns.Contains(key)&&plugin.Mutate(c=>{c.SoundPromptReturns.Add(key);if(c.SoundPromptReturns.Count>5000)c.SoundPromptReturns.RemoveAt(0);}))Event("reminderReturned");}}
  if(poll is not null||Environment.TickCount64<nextPoll)return;
  if(plugin.Configuration.AudioEvents.Any(e=>Core.LocalState.Parse(e.AtUtc)<plugin.Now.AddSeconds(-10)))plugin.Mutate(c=>c.AudioEvents.RemoveAll(e=>Core.LocalState.Parse(e.AtUtc)<plugin.Now.AddSeconds(-10)));
  nextPoll=Environment.TickCount64+1500;pollRevision=revision;submitted=plugin.Configuration.AudioResults.Take(20).ToList();submittedEvents=plugin.Configuration.AudioEvents.Take(30).ToList();poll=Poll(token,submitted,submittedEvents,player.Active||downloading is not null);
 }
 private async Task<AudioPoll> Poll(string credential,List<AudioResult> results,List<AudioEvent> events,bool busy){var report=cache.Report();using var request=new HttpRequestMessage(HttpMethod.Post,Core.ServiceClient.Endpoint(Core.ServiceClient.BaseUrl,"/api/device/comms"));request.Headers.Authorization=new("Bearer",credential);request.Content=new StringContent(JsonSerializer.Serialize(new{op="audioPoll",data=new{results,events,busy,libraryScope=cache.Manifest?.Scope??"",libraryRevision=cache.Manifest?.Revision??0,cache=report}},Core.ServiceClient.Json),System.Text.Encoding.UTF8,"application/json");using var response=await http.SendAsync(request,cancel.Token);response.EnsureSuccessStatusCode();return JsonSerializer.Deserialize<AudioPoll>(await response.Content.ReadAsStringAsync(cancel.Token),Core.ServiceClient.Json)!;}
 public void Dispose()
 {
  if(disposed)return;disposed=true;allowed=confirmed=false;
  try{Stop();}finally
  {
   try{CancelDownload();}finally
   {
    try{cancel.Cancel();}finally{try{http.Dispose();}finally{cancel.Dispose();player.Dispose();}}
   }
  }
 }
}
