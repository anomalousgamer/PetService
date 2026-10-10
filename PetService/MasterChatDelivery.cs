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
    internal static SeString RgbMessage(string text,string hex,string name)
    {
        var rgb=Convert.ToUInt32(hex.TrimStart('#'),16);
        var builder=new Lumina.Text.SeStringBuilder();
        builder.PushColorRgba((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb,255);
        builder.Append("["+name+"] "+text);builder.PopColor();
        return SeString.Parse(builder.ToReadOnlySeString().Data.Span);
    }
    internal static void Print(Configuration configuration,string text)
    {
        Plugin.Chat.Print(new XivChatEntry{Type=configuration.MasterChatType=="system"?XivChatType.SystemMessage:XivChatType.Echo,Message=System.Text.RegularExpressions.Regex.IsMatch(configuration.MasterChatRgb??"","^#[0-9a-fA-F]{6}$")?RgbMessage(text,configuration.MasterChatRgb!,configuration.Features.Identity.Name):Message(text,configuration.MasterChatColour,configuration.Features.Identity.Name),Silent=true});
        if(configuration.MasterChatSound)PlaySound();
    }
    private static unsafe void PlaySound(){try{UIGlobals.PlayChatSoundEffect(1);}catch{}}
}
