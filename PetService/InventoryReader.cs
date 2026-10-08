using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using PetService.Core;
namespace PetService;
internal static unsafe class InventoryReader
{
 internal static InventorySnapshot Capture(Plugin plugin)
 {
  var manager=InventoryManager.Instance();if(manager==null)throw new InvalidOperationException();
  var result=new InventorySnapshot{CharacterId=plugin.CurrentCharacterId,CharacterName=Plugin.Player.CharacterName,HomeWorld=Plugin.Player.HomeWorld.Value.Name.ToString(),CapturedAtUtc=LocalState.Stamp(plugin.Now),Gil=manager->GetGil()};
  foreach(var type in Enum.GetValues<InventoryType>()) {
   var name=type.ToString();if(name.Contains("FreeCompany")||name.Contains("Housing")||name.Contains("Trade")||name.Contains("Currency"))continue;
   if(!(name.StartsWith("Inventory")||name.StartsWith("Armory")||name is "EquippedItems" or "Crystals" or "KeyItems" || name.Contains("SaddleBag")||name.StartsWith("Retainer")))continue;
   var container=manager->GetInventoryContainer(type);var bag=new InventoryBag{CapturedAtUtc=result.CapturedAtUtc,Id=name,Name=name,Available=container!=null && container->IsLoaded,Capacity=container==null?0:container->Size};
   if(name.StartsWith("Retainer")) {
    var retainers=RetainerManager.Instance();var active=retainers==null?null:retainers->GetActiveRetainer();
    if(active==null)continue;bag.Id="retainer:"+active->RetainerId+":"+name;bag.Name="Retainer "+active->Name.ToString()+" · "+name;
   }
   if(bag.Available)for(var i=0;i<bag.Capacity;i++) {
    var item=container->GetInventorySlot(i);var id=item==null?0u:item->IsSymbolic?item->GetBaseItemId():item->ItemId%1000000;var row=Plugin.Data.GetExcelSheet<Item>()?.GetRowOrDefault(id);
    bag.Slots.Add(new(){Slot=i+1,ItemId=id,Quantity=item==null?0u:(uint)item->Quantity,Hq=item!=null && (item->Flags & InventoryItem.ItemFlags.HighQuality)!=0,Name=id==0?"":row?.Name.ToString()??("Item "+id),IconId=row?.Icon??0});
   }
   if(bag.Id.StartsWith("retainer:")&&!bag.Available)continue;
   result.Containers.Add(bag);
  }
  var old=plugin.Configuration.Features.Inventory.FirstOrDefault(x=>x.CharacterId==result.CharacterId);
  if(old is not null)foreach(var bag in old.Containers.Where(b=>b.Id.StartsWith("retainer:")&&!result.Containers.Any(n=>n.Id==b.Id)))result.Containers.Add(bag);
  return result;
 }
}
