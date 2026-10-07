using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PetService.Core;

// Dalamud saves configuration with Newtonsoft; service requests use
// System.Text.Json. Keep nested buffered item data identical across both.
internal sealed class ActivityDataConverter : JsonConverter<Dictionary<string,object?>>
{
    public override Dictionary<string,object?> ReadJson(JsonReader reader,Type type,Dictionary<string,object?>? existing,bool hasExisting,JsonSerializer serializer)
    {
        var dateParsing=reader.DateParseHandling;
        JToken token;
        try{reader.DateParseHandling=DateParseHandling.None;token=JToken.Load(reader);}
        finally{reader.DateParseHandling=dateParsing;}
        if(token.Type==JTokenType.Null)return [];
        using var document=System.Text.Json.JsonDocument.Parse(token.ToString(Formatting.None));
        return document.RootElement.EnumerateObject().ToDictionary(p=>p.Name,p=>(object?)p.Value.Clone());
    }
    public override void WriteJson(JsonWriter writer,Dictionary<string,object?>? value,JsonSerializer serializer)
        =>writer.WriteRawValue(System.Text.Json.JsonSerializer.Serialize(value??[],ServiceClient.Json));
}
