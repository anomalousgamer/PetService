using System.Text.Json;

namespace PetService.Core;

internal sealed record TradeDisplay(string Outcome,string GiveItems,string ReceiveItems,string GiveGil,string ReceiveGil,string Detail,bool IsModern);
internal static class TradePresentation
{
    internal static TradeDisplay Read(ActivityRecord record,Func<uint,string>? resolve=null)
    {
        var d=JsonSerializer.SerializeToElement(record.Data,ServiceClient.Json);
        string Text(string key)=>d.TryGetProperty(key,out var v)?v.GetString()??"":"";
        var modern=d.TryGetProperty("tradeId",out _);
        var known=!d.TryGetProperty("offersKnown",out var knownValue)||knownValue.ValueKind==JsonValueKind.True;
        string Items(string key) {
            if(!known)return "Not observed";
            if(!d.TryGetProperty(key,out var values)||values.ValueKind!=JsonValueKind.Array)return "Not recorded";
            var lines=new List<string>();
            foreach(var item in values.EnumerateArray()) {
                var id=item.TryGetProperty("itemId",out var idValue)&&idValue.ValueKind==JsonValueKind.Number&&idValue.TryGetUInt32(out var itemId)?itemId:0;
                var name=item.TryGetProperty("name",out var n)&&n.ValueKind==JsonValueKind.String?n.GetString():null;
                if(string.IsNullOrEmpty(name))name=resolve?.Invoke(id)??$"Item {id}";
                var hq=item.TryGetProperty("hq",out var h)&&h.ValueKind==JsonValueKind.True?" (HQ)":"";
                var qty=item.TryGetProperty("quantity",out var q)&&q.ValueKind==JsonValueKind.Number&&q.TryGetUInt32(out var count)?count.ToString("N0"):"?";
                lines.Add(name+hq+" × "+qty);
            }
            return lines.Count==0?"None":string.Join('\n',lines);
        }
        string Gil(string key)=>known&&d.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetUInt32(out var gil)?gil.ToString("N0")+" gil":"Not recorded";
        var outcome=Text("outcome") switch{"completed"=>"Completed","cancelled"=>"Cancelled","failed"=>"Failed","interrupted"=>"Interrupted",_=>"Completion unconfirmed"};
        var phase=Text("phase");
        var detail=modern?(outcome=="Completed"?"Trade result observed. Amounts are from the trade offers.":"Amounts shown are offered amounts; this record does not confirm a transfer."):
            "Earlier offer record. Quantities, gil and completion were not recorded.";
        if(Text("reason") is {Length:>0} reason)detail+=" "+reason+".";
        return new(outcome,Items(modern?"giveItems":phase=="give-offer-observed"?"items":"__unknown"),
            Items(modern?"receiveItems":phase=="receive-offer-observed"?"items":"__unknown"),Gil("giveGil"),Gil("receiveGil"),detail,modern);
    }
}
