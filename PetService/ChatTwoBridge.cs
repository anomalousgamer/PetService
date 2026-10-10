using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Newtonsoft.Json.Linq;
using System.Runtime.CompilerServices;

namespace PetService;

// Only input visibility/focus are used. No draft, message or recipient is read.
internal sealed class ChatTwoBridge(IDalamudPluginInterface plugin)
{
    private readonly ICallGateSubscriber<object> state = plugin.GetIpcSubscriber<object>("ChatTwo.GetChatInputState");
    private long nextRead;
    internal bool Available { get; private set; }
    internal bool Focused { get; private set; }
    internal void Update()
    {
        if(Environment.TickCount64 < nextRead)return;
        nextRead=Environment.TickCount64+50;
        try {
            var value=state.InvokeFunc();
            // Dalamud converts differing IPC return types through JSON.
            bool visible=false,focused=false;
            if(value is ITuple tuple && tuple.Length>=2){visible=tuple[0] is true;focused=tuple[1] is true;}
            else if(value is JObject json){visible=json.Value<bool?>("Item1")??json.Value<bool?>("InputVisible")??false;focused=json.Value<bool?>("Item2")??json.Value<bool?>("InputFocused")??false;}
            else {Available=false;Focused=false;return;}
            Available=true;Focused=visible&&focused;
        } catch {Available=false;Focused=false;nextRead=Environment.TickCount64+2000;}
    }
}
