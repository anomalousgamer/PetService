using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;

namespace PetService;

// Makes ImGui text editors read-only during Sleep, independent of plugin draw
// order. Game/native chat sends are separately guarded by ChatCommandGuard.
internal sealed unsafe class SleepInputGuard : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte InputText(byte* label,byte* hint,byte* buffer,int size,Vector2 dimensions,ImGuiInputTextFlags flags,nint callback,nint userData);
    private Hook<InputText>? hook;
    private nint library;
    private readonly Func<bool> sleeping;
    internal bool Available=>hook is {IsEnabled:true,IsDisposed:false};
    internal SleepInputGuard(IGameInteropProvider interop,Func<bool> sleeping,IPluginLog log)
    {
        this.sleeping=sleeping;
        try {
            if(!NativeLibrary.TryLoad("cimgui",typeof(ImGui).Assembly,DllImportSearchPath.SafeDirectories,out library)
                && !NativeLibrary.TryLoad(Path.Combine(Path.GetDirectoryName(typeof(ImGui).Assembly.Location)!,"cimgui.dll"),out library))return;
            if(!NativeLibrary.TryGetExport(library,"igInputTextEx",out var address))return;
            hook=interop.HookFromAddress<InputText>(address,DrawText);hook.Enable();
        } catch(Exception error){hook?.Dispose();hook=null;log.Error(error,"Sleep text guard unavailable.");}
    }
    private byte DrawText(byte* label,byte* hint,byte* buffer,int size,Vector2 dimensions,ImGuiInputTextFlags flags,nint callback,nint userData)
    {
        if(!sleeping())return hook!.Original(label,hint,buffer,size,dimensions,flags,callback,userData);
        var io=ImGui.GetIO();io.ClearInputCharacters();io.ClearInputKeys();
        flags=(flags|ImGuiInputTextFlags.ReadOnly)&~(ImGuiInputTextFlags.EnterReturnsTrue|ImGuiInputTextFlags.CallbackEdit|ImGuiInputTextFlags.CallbackAlways|ImGuiInputTextFlags.CallbackCompletion|ImGuiInputTextFlags.CallbackHistory);
        hook!.Original(label,hint,buffer,size,dimensions,flags,callback,userData);
        return 0;
    }
    public void Dispose(){hook?.Dispose();if(library!=0)NativeLibrary.Free(library);}
}
