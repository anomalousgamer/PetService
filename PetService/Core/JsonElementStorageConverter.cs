using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.Json;
namespace PetService.Core;
internal sealed class JsonElementStorageConverter:Newtonsoft.Json.JsonConverter
{
 public override bool CanConvert(Type type)=>type==typeof(JsonElement)||type==typeof(JsonElement?);
 public override object? ReadJson(JsonReader reader,Type type,object? existing,Newtonsoft.Json.JsonSerializer serializer)
 {
  var previous=reader.DateParseHandling;JToken token;
  try{reader.DateParseHandling=DateParseHandling.None;token=JToken.Load(reader);}finally{reader.DateParseHandling=previous;}
  if(token.Type==JTokenType.Null&&type==typeof(JsonElement?))return null;
  using var document=JsonDocument.Parse(token.ToString(Formatting.None));return document.RootElement.Clone();
 }
 public override void WriteJson(JsonWriter writer,object? value,Newtonsoft.Json.JsonSerializer serializer)
 {
  if(value is not JsonElement element||element.ValueKind==JsonValueKind.Undefined){writer.WriteNull();return;}
  writer.WriteRawValue(element.GetRawText());
 }
}
