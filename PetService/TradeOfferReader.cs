using FFXIVClientStructs.FFXIV.Client.Game;
using PetService.Core;

namespace PetService;

internal static unsafe class TradeOfferReader
{
    internal static TradeSide Read(Span<InventoryItem> slots,Func<uint,string> name)
    {
        if(slots.Length!=6)throw new ArgumentException("A trade side has six native slots.",nameof(slots));
        var items=new List<TradeItem>();
        for(var i=0;i<5;i++) {
            ref var item=ref slots[i];
            var id=item.IsSymbolic?item.GetBaseItemId():item.ItemId%1000000;
            if(id==0)continue;
            var label=name(id);
            items.Add(new(id,string.IsNullOrEmpty(label)?$"Item {id}":label,
                item.Quantity>0?(uint)item.Quantity:null,(item.Flags&InventoryItem.ItemFlags.HighQuality)!=0 || (!item.IsSymbolic && item.ItemId is >=1000000 and <2000000),i+1));
        }
        // The sixth trade slot is the offer, not the character's wallet.
        ref var gil=ref slots[5];
        return new(items,gil.Quantity>=0?(uint)gil.Quantity:null);
    }
}
