using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PetService.Core;
public sealed class ServiceFailure(string code, bool revoked = false) : Exception(code)
{
    public bool Revoked { get; } = revoked;
}
public sealed class ServiceClient : IDisposable
{
    public const string BaseUrl = "https://brandonnissen.com/pet-service-test";
    public static readonly JsonSerializerOptions Json = new()
    { PropertyNamingPolicy=JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive=true, DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull };
    private readonly HttpClient http;
    public ServiceClient(HttpMessageHandler? handler=null){http=new(handler??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(12)};}
    public static Uri Endpoint(string serviceUrl, string route)
    {
        if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) throw new ServiceFailure("Use an HTTPS service URL without a key, query, or fragment.");
        return new Uri(serviceUrl.TrimEnd('/') + route);
    }
    public static bool MatchesBundledService(string? serviceUrl)
    {
        try { return Endpoint(serviceUrl ?? "", "/api/sync") == Endpoint(BaseUrl, "/api/sync"); }
        catch (ServiceFailure) { return false; }
    }
    private async Task<T> Post<T>(string route, object payload, string? token, CancellationToken cancel)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        cancel=deadline.Token;
        using var request=new HttpRequestMessage(HttpMethod.Post, Endpoint(BaseUrl,route));
        request.Content=new StringContent(JsonSerializer.Serialize(payload,Json),Encoding.UTF8,"application/json");
        if (token is not null) request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancel).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            var code="SERVICE_UNAVAILABLE";
            if(response.Content.Headers.ContentLength is null or <=8192 && response.Content.Headers.ContentType?.MediaType=="application/json") {
                await using var errorStream=await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
                using var errorBody=new MemoryStream();var errorChunk=new byte[1024];
                while(errorBody.Length<=8192){var count=await errorStream.ReadAsync(errorChunk.AsMemory(0,(int)Math.Min(errorChunk.Length,8193-errorBody.Length)),cancel).ConfigureAwait(false);if(count==0)break;errorBody.Write(errorChunk,0,count);}
                if(errorBody.Length<=8192)try{using var doc=JsonDocument.Parse(errorBody.ToArray());var value=doc.RootElement.GetProperty("code").GetString();if(value is not null && value.Length<=80 && value.All(c=>char.IsAsciiLetterUpper(c)||c=='_'))code=value;}catch{}
            }
            throw new ServiceFailure(code,token is not null && response.StatusCode==HttpStatusCode.Unauthorized && code is "DEVICE_NOT_AUTHORIZED" or "DEVICE_REVOKED" or "DEVICE_REPLACED" or "PET_ARCHIVED");
        }
        if (response.Content.Headers.ContentLength is > 4194304) throw new ServiceFailure("Service response is too large.");
        await using var stream=await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using var buffer=new MemoryStream();
        var chunk=new byte[8192]; int read;
        while ((read=await stream.ReadAsync(chunk,cancel).ConfigureAwait(false))>0)
        { if(buffer.Length+read>4194304) throw new ServiceFailure("Service response is too large."); await buffer.WriteAsync(chunk.AsMemory(0,read),cancel).ConfigureAwait(false); }
        return JsonSerializer.Deserialize<T>(buffer.ToArray(),Json) ?? throw new ServiceFailure("Invalid service response.");
    }
    public Task<PairResult> Pair(string code,CancellationToken cancel) => Post<PairResult>("/api/pair",new { code },null,cancel);
    public Task<SyncResult> Sync(string token,Observation? status,List<Choice> events,List<ActivityRecord> activity,string? reportRequestId,object access,InventorySnapshot? inventory,object? catalog,List<FeatureResult> results,List<TaskEvent> taskEvents,CancellationToken cancel,List<IconAsset>? icons=null)
    {
        var snapshotIssues=new Dictionary<string,string>();
        if(inventory is not null && Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(inventory,Json))>1200000){inventory=null;snapshotIssues["inventory"]="INVENTORY_TOO_LARGE";}
        if(catalog is not null && Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(catalog,Json))>300000){catalog=null;snapshotIssues["catalog"]="CATALOG_TOO_LARGE";}
        return Post<SyncResult>("/api/sync",new { status,events,activity,reportRequestId,access,inventory=inventory is null?null:new {inventory.CharacterId,inventory.CharacterName,inventory.HomeWorld,inventory.CapturedAtUtc,inventory.Gil,inventory.Containers},catalog,results,taskEvents,icons,snapshotIssues=snapshotIssues.Count==0?null:snapshotIssues },token,cancel);
    }
    public Task<DeviceEventResult> DeviceEvents(string token,List<Choice> events,CancellationToken cancel,List<FeatureResult>? results=null) => Post<DeviceEventResult>("/api/device/events",new { events,results },token,cancel);
    public Task<CredentialState> Verify(string token,CancellationToken cancel)=>Post<CredentialState>("/api/device/verify",new {},token,cancel);
    public async Task<byte[]> Portrait(string token,CancellationToken cancel){using var r=new HttpRequestMessage(HttpMethod.Get,Endpoint(BaseUrl,"/api/device/portrait"));r.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);using var response=await http.SendAsync(r,cancel);response.EnsureSuccessStatusCode();var b=await response.Content.ReadAsByteArrayAsync(cancel);if(b.Length>2000000)throw new ServiceFailure("PORTRAIT_TOO_LARGE");return b;}
    public void Dispose() => http.Dispose();
}
