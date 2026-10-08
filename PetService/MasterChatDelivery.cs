using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.UI;
using PetService.Core;

namespace PetService;
internal static class MasterChatDelivery
{
    internal static SeString Message(string text,ushort colour,string name="Master")
    {
        var builder=new SeStringBuilder();
        if(colour>0)builder.AddUiForeground(colour);
        builder.AddText("["+name+"] "+text);
        if(colour>0)builder.AddUiForegroundOff();
        return builder.Build();
    }
    internal static void Print(Configuration configuration,string text)
    {
        Plugin.Chat.Print(new XivChatEntry{Type=configuration.MasterChatType=="system"?XivChatType.SystemMessage:XivChatType.Echo,Message=Message(text,configuration.MasterChatColour,configuration.Features.Identity.Name),Silent=true});
        if(configuration.MasterChatSound)PlaySound();
    }
    private static unsafe void PlaySound(){try{UIGlobals.PlayChatSoundEffect(1);}catch{}}
}
