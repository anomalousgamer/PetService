using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PetService.Core;
namespace PetService;
internal sealed class PrivateChatSession(Plugin plugin):IDisposable
{
 private readonly HttpClient http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(12)};
 private readonly CancellationTokenSource cancel=new();private Task<JsonElement>? work;private Action<JsonElement>? completed;private long nextPoll;private string password="",vaultKey="";private bool enrolled,refreshFirst,disposed;private string pendingRoom="";private bool vaultSaved=true;
 private sealed record PendingCall(string Op,object Data,Action<JsonElement>? Completed);
 private readonly Queue<PendingCall> pendingCalls=new();private string workOperation="";private long nextErrorLog;
 private Task<JsonElement>? incoming;private CancellationTokenSource? incomingCancel;private long nextIncomingAttempt;
 private long preferenceRevision;private int pendingPreferences;private ChatPreferences confirmedPreferences=new();
 internal bool PreferencesSaving=>pendingPreferences>0;
 internal bool MutationBusy=>pendingCalls.Count>0||work is not null&&workOperation!="state";
 internal bool Ready=>!Paused&&Vault is not null&&enrolled&&State.Devices.Any(d=>d.Id==Vault.Identity.Id);
 internal void ReportError(string operation,Exception error){Error=error.Message;if(Environment.TickCount64<nextErrorLog)return;nextErrorLog=Environment.TickCount64+30000;try{Plugin.Log.Warning(error,"Pet Service private chat failed: {Operation}.",operation);}catch{}}
 internal bool Focused{get;set;}=true;
 internal bool Paused=>disposed||!Master&&(!plugin.IsPaired||!plugin.CharacterAllowed);
 internal ChatPreferences Preferences{get;private set;}=new();
 internal bool ConversationPaused(string id)=>Preferences.GlobalPaused||Preferences.Paused.Contains(id);
 internal void Preference(string? room=null,bool? muted=null,bool? paused=null,bool? global=null)
 {
  if(Paused||Vault is null)return;
  var revision=++preferenceRevision;pendingPreferences++;
  var desired=new ChatPreferences{GlobalPaused=global??Preferences.GlobalPaused,Muted=Preferences.Muted.ToList(),Paused=Preferences.Paused.ToList()};
  if(room is not null){if(muted is { } m){desired.Muted.Remove(room);if(m)desired.Muted.Add(room);}if(paused is { } p){desired.Paused.Remove(room);if(p)desired.Paused.Add(room);}}
  Preferences=desired;
  Start("preferences",new{threadId=room,muted,paused,globalPaused=global},v=>{confirmedPreferences=v.Deserialize<ChatPreferences>(ServiceClient.Json)!;pendingPreferences=Math.Max(0,pendingPreferences-1);if(revision==preferenceRevision)Preferences=confirmedPreferences;nextPoll=0;});
 }
 internal bool HasCredential=>password.Length>0;
 internal bool Master{get;private set;}internal string Pet{get;private set;}="";internal string Room{get;private set;}="";internal string Error{get;set;}="";internal bool Busy=>work is not null;internal ChatVault? Vault{get;private set;}internal ChatState State{get;private set;}=new();internal List<ChatMessage> Messages{get;}=[];internal bool More{get;private set;}internal string Draft{get;set;}="";internal string TitleDraft{get;set;}="New conversation";internal string Notice{get;set;}="";
 internal void SetAccount(bool master,string pet,string secret){if(Master==master&&Pet==pet&&password==secret&&Vault is not null)return;ResetWork();Master=master;Pet=pet;password=secret;Vault=null;enrolled=false;Room="";Draft="";Messages.Clear();State=new();Preferences=new();confirmedPreferences=new();pendingPreferences=0;preferenceRevision++;Error="";Notice="";nextPoll=0;}
 private async Task<JsonElement> Request(string op,object data,CancellationToken? requestCancellation=null){var token=requestCancellation??cancel.Token;using var request=new HttpRequestMessage(HttpMethod.Post,ServiceClient.Endpoint(ServiceClient.BaseUrl,Master?"/api/admin/comms":"/api/device/comms"));request.Headers.Authorization=new(Master?"Basic":"Bearer",Master?Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:"+password)):plugin.Configuration.DeviceToken);request.Content=new StringContent(JsonSerializer.Serialize(new{pet=Master?Pet:null,op,data},ServiceClient.Json),Encoding.UTF8,"application/json");using var response=await http.SendAsync(request,token).ConfigureAwait(false);var bytes=await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);if(bytes.Length>16000000)throw new InvalidOperationException("Chat response too large.");using var json=JsonDocument.Parse(bytes);if(!response.IsSuccessStatusCode)throw new InvalidOperationException(json.RootElement.TryGetProperty("code",out var code)?code.GetString():"Chat service unavailable.");return json.RootElement.Clone();}
 private void ResetWork(){StopIncoming();if(work is not null){_ = work.ContinueWith(t=>{_ = t.Exception;},TaskContinuationOptions.OnlyOnFaulted);work=null;completed=null;}pendingRoom="";workOperation="";pendingCalls.Clear();}
 private void Start(string op,object data,Action<JsonElement>? after=null){if(Paused)return;if(Busy){if(op!="state"){pendingCalls.Enqueue(new(op,data,after));nextPoll=0;}return;}workOperation=op;work=Request(op,data);completed=after;pendingRoom=JsonSerializer.SerializeToElement(data,ServiceClient.Json).TryGetProperty("id",out var id)?id.GetString()??"":"";}
 private void StopIncoming(){var previous=incoming;incoming=null;try{incomingCancel?.Cancel();}finally{incomingCancel?.Dispose();incomingCancel=null;}if(previous is not null)_=previous.ContinueWith(t=>{_=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
 private void UpdateIncoming(bool visible)
 {
  if(!visible||!Focused||Paused||!Ready){StopIncoming();return;}
  if(incoming is {IsCompleted:true}){
   var finished=incoming;incoming=null;incomingCancel?.Dispose();incomingCancel=null;
   try{var value=finished.GetAwaiter().GetResult();if(value.TryGetProperty("revision",out var revision)&&revision.GetString()!=State.Revision)nextPoll=0;nextIncomingAttempt=0;}
   catch(OperationCanceledException){nextIncomingAttempt=Environment.TickCount64+1000;}
   catch(Exception error){nextIncomingAttempt=Environment.TickCount64+5000;ReportError("the live chat connection",error);}
  }
  if(incoming is null&&!Busy&&Environment.TickCount64>=nextIncomingAttempt){incomingCancel=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);incoming=Request("wait",new{deviceId=Vault!.Identity.Id,after=State.Revision},incomingCancel.Token);}
 }
 internal void Save(){if(Vault is null)return;vaultSaved=false;var encrypted=PrivateChatCrypto.Store(Vault);if(!plugin.Mutate(c=>c.PrivateChatVaults[vaultKey]=encrypted))throw new InvalidOperationException(plugin.SaveError);vaultSaved=true;}
 internal void Tick(bool visible)
 {
  if(Paused){StopIncoming();return;}
  if(!Master&&(Pet!=plugin.Configuration.PetName||Vault is not null&&Vault.Scope!=plugin.Configuration.PetId))SetAccount(false,plugin.Configuration.PetName,"");
  if(Vault is not null&&!vaultSaved){if(Environment.TickCount64<nextPoll)return;try{Save();Error="";}catch{Error="Could not save protected chat storage. Sending is paused until saving succeeds.";nextPoll=Environment.TickCount64+5000;return;}}
  if(work is {IsCompleted:true}){var old=work;var finish=completed;var failedRoom=pendingRoom;var operation=workOperation;work=null;completed=null;workOperation="";try{var value=old.GetAwaiter().GetResult();Error="";finish?.Invoke(value);}catch(Exception e){if(operation=="preferences"){pendingPreferences=Math.Max(0,pendingPreferences-1);if(pendingPreferences==0)Preferences=confirmedPreferences;}ReportError(operation,e);nextPoll=Environment.TickCount64+5000;if(Error is "CHAT_RESET" or "CHAT_NOT_FOUND"){if(Vault is not null&&Vault.Outbox.Count>0){Vault.Outbox.RemoveAll(x=>x.Data.TryGetProperty("id",out var id)&&id.GetString()==failedRoom);Save();}Messages.Clear();Draft="";refreshFirst=true;}else if(Error=="CHAT_DEVICE_REQUIRED"){Vault=null;enrolled=false;Room="";Draft="";Messages.Clear();State=new();}else if(Error=="CHAT_PAUSED"){refreshFirst=true;nextPoll=0;}else if(Error=="CHAT_CHANGED"&&Vault is not null){Vault.Outbox.RemoveAll(x=>x.Op=="rename"&&x.Data.TryGetProperty("id",out var id)&&id.GetString()==failedRoom);Notice="Conversation changed on another device. Review its title before renaming again.";Save();refreshFirst=true;nextPoll=0;}}}
  UpdateIncoming(visible);
  if(Busy||Environment.TickCount64<nextPoll||password.Length==0&&Master||!Master&&!plugin.IsPaired)return;
  if(pendingCalls.TryDequeue(out var pending)){Start(pending.Op,pending.Data,pending.Completed);return;}
  if(Vault is null){if(!Focused)return;Start("whoami",new{},value=>{var scope=value.GetProperty("petId").GetString()!;vaultKey=(Master?"master:":"pet:")+scope;Vault=plugin.Configuration.PrivateChatVaults.TryGetValue(vaultKey,out var encrypted)?PrivateChatCrypto.Load(encrypted):new(){Scope=scope,Role=Master?"master":"pet",Identity=PrivateChatCrypto.Identity(),Personas=[new(){Name=Master?"Master":"Pet"}]};if(Vault.Scope!=scope||Vault.Role!=(Master?"master":"pet"))throw new InvalidOperationException("Chat identity belongs to another participant.");ApplyEpoch(value.GetProperty("epoch").GetInt32());confirmedPreferences=value.GetProperty("preferences").Deserialize<ChatPreferences>(ServiceClient.Json)!;if(pendingPreferences==0)Preferences=confirmedPreferences;Save();});return;}
  if(!enrolled){if(!Focused)return;Start("enroll",new{id=Vault.Identity.Id,publicKey=Vault.Identity.PublicKey,signKey=Vault.Identity.SignKey},_=>{enrolled=true;refreshFirst=true;});return;}
  if(refreshFirst){refreshFirst=false;Poll(false,visible);return;}
  if(Vault.Outbox.OrderBy(x=>x.Op is "delete" or "clearAll"?0:1).FirstOrDefault(x=>x.Op!="message"||!x.Data.TryGetProperty("id",out var id)||!ConversationPaused(id.GetString()??"")) is { } queued){Start(queued.Op,queued.Data,_=>{Accepted(queued);Vault.Outbox.Remove(queued);Save();nextPoll=0;});return;}
  if(!Focused||Environment.TickCount64<nextPoll)return;Poll(false,visible);
 }
 internal void Poll(bool older,bool transcript=true){if(Busy||Vault is null||!enrolled)return;var requestedRoom=transcript?Room:"";nextPoll=Environment.TickCount64+(transcript?(incoming is not null&&nextIncomingAttempt==0?30000:1000):5000);Start("state",new{deviceId=Vault.Identity.Id,threadId=requestedRoom.Length>0?requestedRoom:null,before=older?Messages.FirstOrDefault()?.Sequence:null,after=!older&&Messages.Count>0?Messages.Last().Sequence:null},value=>{
  var state=JsonSerializer.Deserialize<ChatState>(value,ServiceClient.Json)!;ChatKeySharing.RememberPairedDevices(Vault!,state);ApplyEpoch(state.Epoch);State=state;RestorePendingConversations();confirmedPreferences=state.Preferences;if(pendingPreferences==0)Preferences=confirmedPreferences;
  var arrivals=0;foreach(var t in state.Threads.Where(t=>!t.Deleted)){if(ConversationPaused(t.Id))continue;var had=Vault!.Notified.TryGetValue(t.Id,out var previous);if(had&&ulong.Parse(t.LastPeerSequence)>ulong.Parse(previous!)&&!Preferences.Muted.Contains(t.Id)&&(!transcript||Room!=t.Id))arrivals++;Vault.Notified[t.Id]=t.LastPeerSequence;}if(arrivals>0)Plugin.Notifications.AddNotification(new Dalamud.Interface.ImGuiNotification.Notification{Title="Private RP chat",Content=arrivals==1?"A conversation has a new message.":arrivals+" conversations have new messages.",Type=Dalamud.Interface.ImGuiNotification.NotificationType.Info});
  foreach(var thread in state.Threads){var previous=Vault.Generations.GetValueOrDefault(thread.Id);if(thread.Generation<previous)throw new InvalidOperationException("Conversation rollback detected.");if(thread.Deleted||previous!=0&&previous!=thread.Generation){foreach(var key in Vault.Keys.Keys.Where(k=>k.StartsWith(thread.Id+":",StringComparison.Ordinal)).ToList())Vault.Keys.Remove(key);Vault.Drafts.Remove(thread.Id);Vault.Outbox.RemoveAll(x=>x.Data.TryGetProperty("id",out var id)&&id.GetString()==thread.Id);if(Room==thread.Id){Messages.Clear();Draft="";}}Vault.Generations[thread.Id]=thread.Generation;}
  if(Room.Length>0&&!state.Threads.Any(t=>t.Id==Room&&!t.Deleted)&&!Vault.Outbox.Any(x=>x.Op=="thread"&&x.Data.TryGetProperty("id",out var pendingId)&&pendingId.GetString()==Room)){Room="";Messages.Clear();Draft="";}
  var empty=Messages.Count==0;if(requestedRoom==Room&&!ConversationPaused(Room))foreach(var message in state.Messages){var thread=State.Threads.FirstOrDefault(t=>t.Id==Room);if(thread is null||thread.Deleted||message.Generation!=thread.Generation||Messages.Any(m=>m.Id==message.Id))continue;try{message.Content=PrivateChatCrypto.Open<ChatContent>(Key(thread),["message",thread.Id,thread.Generation,message.Id],message.Packet,Trusted(message.Packet.DeviceId));Messages.Add(message);}catch(Exception e){ReportError("decrypting an incoming message",e);}}
  Messages.Sort((a,b)=>ulong.Parse(a.Sequence).CompareTo(ulong.Parse(b.Sequence)));if(requestedRoom==Room&&(older||empty))More=state.More;QueueMissingKeys();Save();
 });}
 private void ApplyEpoch(int epoch){if(Vault is null)return;if(epoch<Vault.Epoch)throw new InvalidOperationException("Conversation rollback detected.");if(epoch>Vault.Epoch){Vault.Keys.Clear();Vault.Notified.Clear();Vault.Drafts.Clear();Vault.Outbox.Clear();Messages.Clear();Draft="";Room="";Vault.Epoch=epoch;}}
 internal ChatDevice Trusted(string id){var d=State.Devices.FirstOrDefault(d=>d.Id==id)??throw new InvalidOperationException("Unknown device.");if(d.Id!=Vault!.Identity.Id&&Vault.Pins.GetValueOrDefault(d.Id)!=d.PublicKey+"|"+d.SignKey)throw new InvalidOperationException("This chat device's public keys changed. Its previous keys remain protected.");return d;}
 internal string Key(ChatThread thread){var id=thread.Id+":"+thread.Generation;if(Vault!.Keys.TryGetValue(id,out var key))return key;var envelope=thread.Envelopes.FirstOrDefault(e=>e.RecipientId==Vault.Identity.Id)??throw new InvalidOperationException("Encrypted history is synchronizing automatically. The device holding its keys will share them when it connects.");key=PrivateChatCrypto.Unwrap(thread.Id,thread.Generation,envelope,Vault.Identity,Trusted(envelope.DeviceId));Vault.Keys[id]=key;return key;}
 private string ReadTitle(ChatThread thread)=>PrivateChatCrypto.Open<ChatContent>(Key(thread),["title",thread.Id,thread.Generation],thread.Title,Trusted(thread.Title.DeviceId)).Title??"Conversation";
 internal string Title(ChatThread thread){try{return ReadTitle(thread);}catch{return "Conversation";}}
 internal string ConversationIssue(ChatThread thread){try{ReadTitle(thread);return "";}catch(Exception error){return error.Message;}}
 internal void Choose(string id){if(Vault is null)return;StoreDraft();Room=id;Messages.Clear();Draft=Vault.Drafts.GetValueOrDefault(id)??"";More=false;nextPoll=0;}
 internal void StoreDraft(){if(Vault is null||Room.Length==0)return;Vault.Drafts[Room]=Draft;Save();}
 private List<ChatEnvelope> Recipients(string key,string id,int generation)=>State.Devices.Where(d=>d.Id==Vault!.Identity.Id||Vault.Pins.GetValueOrDefault(d.Id)==d.PublicKey+"|"+d.SignKey).Select(d=>PrivateChatCrypto.Wrap(key,id,generation,d,Vault!.Identity)).ToList();
 private JsonElement WithEpoch(object data){var value=JsonSerializer.SerializeToNode(data,ServiceClient.Json)!.AsObject();value["epoch"]=Vault!.Epoch;return JsonSerializer.SerializeToElement(value,ServiceClient.Json);}
 private void Queue(string op,object data){if(Vault is null||Paused)return;Vault.Outbox.Add(new(){Op=op,Data=WithEpoch(data)});Save();nextPoll=0;}
 internal void Create(){if(!Ready||Vault is null||string.IsNullOrWhiteSpace(TitleDraft))return;var id=Guid.NewGuid().ToString();var key=PrivateChatCrypto.NewKey();Vault.Keys[id+":1"]=key;Vault.Generations[id]=1;Queue("thread",new{id,deviceId=Vault.Identity.Id,title=PrivateChatCrypto.Seal(key,["title",id,1],new{title=TitleDraft.Trim()},Vault.Identity),envelopes=Recipients(key,id,1)});RestorePendingConversations();Choose(id);Notice="New conversation created. Connecting…";}
 private void RestorePendingConversations(){if(Vault is null)return;foreach(var item in Vault.Outbox.Where(q=>q.Op=="thread")){var id=item.Data.GetProperty("id").GetString()!;if(State.Threads.Any(t=>t.Id==id))continue;State.Threads.Insert(0,new(){Id=id,Title=item.Data.GetProperty("title").Deserialize<ChatPacket>(ServiceClient.Json)!,Envelopes=item.Data.GetProperty("envelopes").Deserialize<List<ChatEnvelope>>(ServiceClient.Json)!});}}
 internal void Send(){if(Paused||ConversationPaused(Room)||Vault is null||string.IsNullOrWhiteSpace(Draft)||Room.Length==0)return;var thread=State.Threads.FirstOrDefault(t=>t.Id==Room&&!t.Deleted);if(thread is null){Notice="Waiting for the conversation to synchronize. Your draft is still here.";return;}if(ConversationIssue(thread).Length>0){Notice="Your draft is saved while encrypted history synchronizes.";return;}var persona=Vault.Personas.FirstOrDefault(p=>p.Id==Vault.Active)??Vault.Personas.First();var id=Guid.NewGuid().ToString();var packet=PrivateChatCrypto.Seal(Key(thread),["message",Room,thread.Generation,id],new{alias=persona.Name,color=persona.Color,text=Draft},Vault.Identity);Queue("message",new{id=Room,generation=thread.Generation,deviceId=Vault.Identity.Id,messageId=id,packet});Draft="";Vault.Drafts.Remove(Room);Save();Notice="Queued";}
 internal void Rename()=>RenameConversation(Room,TitleDraft);
 internal void RenameConversation(string id,string name){var t=State.Threads.FirstOrDefault(x=>x.Id==id&&!x.Deleted)??throw new InvalidOperationException("This conversation is no longer available.");if(string.IsNullOrWhiteSpace(name))throw new InvalidOperationException("Enter a conversation name.");Queue("rename",new{id,generation=t.Generation,deviceId=Vault!.Identity.Id,revision=t.Revision,title=PrivateChatCrypto.Seal(Key(t),["title",id,t.Generation],new{title=name.Trim()},Vault.Identity)});Notice="Rename queued.";}
 internal void Clear(bool delete,bool all=false){if((!Master&&!delete)||Vault is null)return;if(all){Queue("clearAll",new{id=Guid.NewGuid().ToString()});Messages.Clear();Draft="";return;}var t=State.Threads.First(x=>x.Id==Room);if(delete){DeleteConversation(t.Id,t.Generation);return;}var name=Title(t);var generation=t.Generation+1;var key=PrivateChatCrypto.NewKey();Queue("clear",new{id=Room,generation=t.Generation,deviceId=Vault.Identity.Id,title=PrivateChatCrypto.Seal(key,["title",Room,generation],new{title=name},Vault.Identity),envelopes=Recipients(key,Room,generation)});Vault.Keys[Room+":"+generation]=key;Messages.Clear();Draft="";Vault.Drafts.Remove(Room);Save();}
 internal bool DeviceVerified(ChatDevice device)=>Vault is not null&&ChatKeySharing.Verified(Vault,device);
 private string VerifiedRoomKey(ChatThread thread){ReadTitle(thread);return Key(thread);}
 private int QueueMissingKeys(string? deviceId=null,bool force=false){if(Vault is null)return 0;var planned=ChatKeySharing.Missing(Vault,State,VerifiedRoomKey,deviceId,force);Vault.Outbox.AddRange(planned);if(planned.Count>0)nextPoll=0;return planned.Count;}
 internal bool DeletionPending(string id)=>Vault?.Outbox.Any(q=>q.Op=="delete"&&q.Data.GetProperty("id").GetString()==id)==true;
 internal void DeleteConversation(string id,int generation)
 {
  if(Vault is null||Paused)throw new InvalidOperationException("Connect to chat before deleting a conversation.");
  var target=State.Threads.FirstOrDefault(t=>t.Id==id&&!t.Deleted)??throw new InvalidOperationException("This conversation is no longer available.");
  if(target.Generation!=generation)throw new InvalidOperationException("The conversation changed. Review it before deleting.");
  if(DeletionPending(id))return;
  Vault.Outbox.RemoveAll(q=>q.Data.TryGetProperty("id",out var item)&&item.GetString()==id);
  Vault.Outbox.Insert(0,new(){Op="delete",Data=WithEpoch(new{id,generation})});Save();nextPoll=0;Notice="Deletion queued. Waiting for the service to confirm.";
 }
 private void Accepted(ChatQueued queued)
 {
  if(queued.Op=="delete"){
   var id=queued.Data.GetProperty("id").GetString()!;State.Threads.RemoveAll(t=>t.Id==id);foreach(var key in Vault!.Keys.Keys.Where(k=>k.StartsWith(id+":",StringComparison.Ordinal)).ToList())Vault.Keys.Remove(key);Vault.Drafts.Remove(id);if(Room==id){Room="";Messages.Clear();Draft="";}Notice="Conversation deleted for both participants.";
  }else if(queued.Op=="share"){
   var id=queued.Data.GetProperty("id").GetString();var thread=State.Threads.FirstOrDefault(t=>t.Id==id);if(thread is not null){var envelopes=queued.Data.GetProperty("envelopes").Deserialize<List<ChatEnvelope>>(ServiceClient.Json)!;thread.Envelopes.RemoveAll(e=>envelopes.Any(x=>x.RecipientId==e.RecipientId));thread.Envelopes.AddRange(envelopes);}
  }else if(queued.Op=="clearAll"){State.Threads.Clear();Messages.Clear();Room="";Draft="";Notice="Conversation history cleared.";}
 }
 internal void Import(string json,string secret){var restored=PrivateChatCrypto.Restore(json,secret);if(Vault is null||restored.Scope!=Vault.Scope||restored.Role!=Vault.Role)throw new InvalidOperationException("This backup belongs to another participant.");Vault=restored;Save();enrolled=false;Room="";Draft="";Messages.Clear();State=new();}
 internal void Lock(){if(Vault is not null)StoreDraft();ResetWork();password="";Vault=null;State=new();Messages.Clear();Draft="";enrolled=false;}
 public void Dispose(){if(disposed)return;disposed=true;ResetWork();password="";Vault=null;try{cancel.Cancel();}finally{try{http.Dispose();}finally{cancel.Dispose();}}}
}
