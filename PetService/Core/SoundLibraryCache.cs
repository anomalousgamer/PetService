using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace PetService.Core;
public sealed class SoundLibraryManifest
{
 public int Format{get;set;}
 public bool Complete{get;set;}
 public string Scope{get;set;}="";
 public long Revision{get;set;}
 public int SoundCount{get;set;}
 public List<UploadedSound>? Sounds{get;set;}
}
public sealed class SoundCacheReport
{
 public string Scope{get;set;}="";
 public long Revision{get;set;}
 public bool Complete{get;set;}
 public int Total{get;set;}
 public int Cached{get;set;}
 public int PendingDeletes{get;set;}
 public string? ReportedAtUtc{get;set;}
}
// Only a complete authenticated library snapshot authorizes removal. Playback expiry
// and network failures never evict files. This directory is owned by this cache.
internal sealed class SoundLibraryCache
{
 private static readonly Regex IdPattern=new(@"\A[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}\z",RegexOptions.Compiled);
 private static readonly Regex HashPattern=new(@"\A[a-f0-9]{64}\z",RegexOptions.Compiled);
 private static readonly Regex ClipPattern=new(@"\A([a-f0-9-]{36})\.([a-f0-9]{64})\.wav\z",RegexOptions.Compiled);
 private readonly string root;private readonly object gate=new();private readonly Dictionary<string,string> verified=[];private readonly Dictionary<string,long> written=[];
 internal SoundLibraryManifest? Manifest{get;private set;}
 internal string Error{get;private set;}="";
 internal SoundLibraryCache(string root){this.root=Path.GetFullPath(root);try{var pointer=File.ReadAllText(Path.Combine(this.root,"active-library.json"));var scope=JsonSerializer.Deserialize<string>(pointer)!;if(!IdPattern.IsMatch(scope))return;var saved=ReadManifest(scope);if(saved is not null){Manifest=saved;CleanupLocked();}}catch{}}
 private string Folder(string scope)=>Path.Combine(root,scope);
 private static SoundLibraryManifest Validate(SoundLibraryManifest source){
  if(source.Format!=1||!source.Complete||!IdPattern.IsMatch(source.Scope)||source.Revision<1||source.Revision>9007199254740991||source.Sounds is null||source.SoundCount!=source.Sounds.Count||source.SoundCount>100||source.SoundCount<0)throw new InvalidOperationException("Incomplete sound library; cached files were kept.");
  var ids=new HashSet<string>();foreach(var sound in source.Sounds)if(!IdPattern.IsMatch(sound.Id)||!HashPattern.IsMatch(sound.Hash)||sound.Size is <44 or >2000000||!double.IsFinite(sound.Seconds)||sound.Seconds is <=0 or >15||!ids.Add(sound.Id))throw new InvalidOperationException("Invalid sound library; cached files were kept.");
  return JsonSerializer.Deserialize<SoundLibraryManifest>(JsonSerializer.Serialize(source,ServiceClient.Json),ServiceClient.Json)!;
 }
 private SoundLibraryManifest? ReadManifest(string scope){var directory=Folder(scope);if(Directory.Exists(directory)&&(File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("The sound cache directory is redirected.");var p=Path.Combine(directory,"manifest.json");if(!File.Exists(p))return null;var saved=Validate(JsonSerializer.Deserialize<SoundLibraryManifest>(File.ReadAllText(p),ServiceClient.Json)!);if(saved.Scope!=scope)throw new InvalidOperationException("Sound library scope mismatch.");return saved;}
 private static void AtomicWrite(string path,byte[] bytes){var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes);file.Flush(true);}File.Move(temporary,path,true);}finally{try{File.Delete(temporary);}catch{}}}
 internal void Apply(SoundLibraryManifest snapshot){lock(gate){
  var next=Validate(snapshot);SoundLibraryManifest? saved=null;try{saved=ReadManifest(next.Scope);}catch(JsonException){}catch(InvalidOperationException){}
  if((Manifest?.Scope==next.Scope&&Manifest.Revision>next.Revision)||saved?.Revision>next.Revision)throw new InvalidOperationException("An older sound library was ignored; cached files were kept.");
  var directory=Folder(next.Scope);Directory.CreateDirectory(directory);if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("The sound cache directory is redirected.");
  // Persist deletion intent before removing files, so cleanup can resume after a crash.
  AtomicWrite(Path.Combine(directory,"manifest.json"),JsonSerializer.SerializeToUtf8Bytes(next,ServiceClient.Json));AtomicWrite(Path.Combine(root,"active-library.json"),JsonSerializer.SerializeToUtf8Bytes(next.Scope));Manifest=next;verified.Clear();
  MigrateLegacyLocked();CleanupLocked();
 }}
 private string ClipPath(string scope,UploadedSound sound)=>Path.Combine(Folder(scope),sound.Id+"."+sound.Hash+".wav");
 private bool DesiredLocked(string scope,UploadedSound sound)=>Manifest?.Scope==scope&&Manifest.Sounds!.Any(s=>s.Id==sound.Id&&s.Hash==sound.Hash&&s.Size==sound.Size);
 internal bool Desired(string scope,UploadedSound sound){lock(gate)return DesiredLocked(scope,sound);}
 private static bool Integrity(UploadedSound sound,byte[] bytes)=>bytes.Length==sound.Size&&Convert.ToHexStringLower(SHA256.HashData(bytes))==sound.Hash;
 internal byte[]? Read(string scope,UploadedSound sound){lock(gate){if(!DesiredLocked(scope,sound))return null;var path=ClipPath(scope,sound);try{var info=new FileInfo(path);if(!info.Exists||info.Length!=sound.Size)return null;var bytes=File.ReadAllBytes(path);if(!Integrity(sound,bytes))return null;verified[sound.Id]=sound.Hash;written[sound.Id]=File.GetLastWriteTimeUtc(path).Ticks;return bytes;}catch{return null;}}}
 internal bool Store(string scope,UploadedSound sound,byte[] bytes){lock(gate){
  // An in-flight download may finish after a delete/replacement. It cannot restore it.
  if(!DesiredLocked(scope,sound))return false;if(!Integrity(sound,bytes))throw new InvalidOperationException("Sound integrity check failed.");
  if(!IsVerifiedLocked(sound,ClipPath(scope,sound)))AtomicWrite(ClipPath(scope,sound),bytes);verified[sound.Id]=sound.Hash;written[sound.Id]=File.GetLastWriteTimeUtc(ClipPath(scope,sound)).Ticks;CleanupLocked();return true;
 }}
 internal UploadedSound? NextDownload(Func<UploadedSound,bool>? eligible=null){lock(gate){if(Manifest?.Sounds is null)return null;foreach(var sound in Manifest.Sounds){var path=ClipPath(Manifest.Scope,sound);if(IsVerifiedLocked(sound,path)||eligible?.Invoke(sound)==false)continue;return sound;}return null;}}
 private bool IsVerifiedLocked(UploadedSound sound,string path){try{var file=new FileInfo(path);return verified.GetValueOrDefault(sound.Id)==sound.Hash&&file.Exists&&file.Length==sound.Size&&written.GetValueOrDefault(sound.Id)==file.LastWriteTimeUtc.Ticks;}catch{return false;}}
 private int MigrateLegacyLocked(){
  var pending=0;if(Manifest is null||!Directory.Exists(root))return pending;
  foreach(var file in Directory.EnumerateFiles(root,"*.wav")){
   var match=ClipPattern.Match(Path.GetFileName(file));if(!match.Success||!IdPattern.IsMatch(match.Groups[1].Value))continue;
   var current=Manifest.Sounds!.FirstOrDefault(s=>s.Id==match.Groups[1].Value);
   try{if(current is not null){var valid=Read(Manifest.Scope,current);if(valid is null&&current.Hash==match.Groups[2].Value){var bytes=File.ReadAllBytes(file);if(Integrity(current,bytes)){AtomicWrite(ClipPath(Manifest.Scope,current),bytes);valid=Read(Manifest.Scope,current);}}if(valid is null)continue;}File.Delete(file);}catch{pending++;}
  }
  return pending;
 }
 private int CleanupLocked(){
  var pending=0;if(Manifest is null)return pending;var directory=Folder(Manifest.Scope);if(!Directory.Exists(directory))return pending;
  foreach(var file in Directory.EnumerateFiles(directory)){
   var name=Path.GetFileName(file);var match=ClipPattern.Match(name);var remove=false;
   if(match.Success&&IdPattern.IsMatch(match.Groups[1].Value)){
    var wanted=Manifest.Sounds!.FirstOrDefault(s=>s.Id==match.Groups[1].Value);remove=wanted is null;
    // Keep a prior version until its verified replacement has been committed.
    if(wanted is not null&&wanted.Hash!=match.Groups[2].Value)remove=IsVerifiedLocked(wanted,ClipPath(Manifest.Scope,wanted))||Read(Manifest.Scope,wanted)is not null;
   }else if(name.EndsWith(".tmp",StringComparison.Ordinal))remove=true;
   if(remove)try{File.Delete(file);}catch{pending++;}
  }
  Error=pending>0?"Sound file removal is pending; it will be retried.":"";return pending;
 }
 internal SoundCacheReport? Report(){lock(gate){if(Manifest is null)return null;var pending=MigrateLegacyLocked()+CleanupLocked();var cached=0;foreach(var sound in Manifest.Sounds!){var path=ClipPath(Manifest.Scope,sound);if(IsVerifiedLocked(sound,path))cached++;}return new(){Scope=Manifest.Scope,Revision=Manifest.Revision,Total=Manifest.SoundCount,Cached=cached,PendingDeletes=pending,Complete=cached==Manifest.SoundCount&&pending==0};}}
 internal void RetryCleanup(){lock(gate){MigrateLegacyLocked();CleanupLocked();}}
}
