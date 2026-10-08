using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
namespace PetService.Windows;
internal sealed class PortraitView:IDisposable
{
 private string revision="";private Task<IDalamudTextureWrap>? pending;private IDalamudTextureWrap? texture;private long retry;private bool preview;
 internal void Set(string current,Func<Task<byte[]>> read)
 {
  if(!preview&&current!=revision){ForgetPending();texture?.Dispose();texture=null;revision=current;retry=0;}
  if(pending is {IsCompleted:true}){var task=pending;pending=null;try{texture?.Dispose();texture=task.GetAwaiter().GetResult();}catch{texture=null;retry=Environment.TickCount64+30000;}}
  if(preview||current.Length==0||pending is not null||texture is not null||Environment.TickCount64<retry)return;
  pending=Load(read);
 }
 private static async Task<IDalamudTextureWrap> Load(Func<Task<byte[]>> read){var bytes=await read();return await Plugin.Textures.CreateFromImageAsync(bytes,"PetService Master portrait");}
 internal void Preview(byte[] bytes){ForgetPending();texture?.Dispose();texture=null;preview=true;pending=Plugin.Textures.CreateFromImageAsync(bytes,"PetService portrait preview");}
 internal void ResetPreview()=>Dispose();
 internal void Draw(float height=130){if(texture is null)return;ImGui.Image(texture.Handle,new Vector2(height*texture.Width/texture.Height,height));}
 private void ForgetPending(){if(pending is not null)_=pending.ContinueWith(t=>{if(t.Status==TaskStatus.RanToCompletion)t.Result.Dispose();else if(t.IsFaulted)_=t.Exception;},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);pending=null;}
 public void Dispose(){ForgetPending();texture?.Dispose();texture=null;revision="";preview=false;retry=0;}
}
