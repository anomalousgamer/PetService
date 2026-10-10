using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Lumina.Excel.Sheets;
namespace PetService;
internal static unsafe class SleepEmote
{
    internal static string? TryApply()
    {
        var emote=Plugin.Data.GetExcelSheet<Emote>().FirstOrDefault(e=>e.TextCommand.ValueNullable?.Command.ToString()=="/playdead");
        var manager=EmoteManager.Instance();
        if(emote.RowId==0||!Plugin.Unlocks.IsEmoteUnlocked(emote))return "Play Dead is not unlocked on this character.";
        if(manager==null||!manager->CanExecuteEmote((ushort)emote.RowId)||!manager->ExecuteEmote((ushort)emote.RowId,null))return "Play Dead cannot be used in the character's current state.";
        return null;
    }
}
