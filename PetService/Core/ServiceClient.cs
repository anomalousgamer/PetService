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
    public static readonly JsonSerializerOptions Json = new()
    { PropertyNamingPolicy=JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive=true, DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull };
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(12) };
    public static Uri Endpoint(string serviceUrl, string route)
    {
        if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) throw new ServiceFailure("Use an HTTPS service URL without a key, query, or fragment.");
        return new Uri(serviceUrl.TrimEnd('/') + route);
    }
    private async Task<T> Post<T>(string url, string route, object payload, string? token, CancellationToken cancel)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        cancel=deadline.Token;
        using var request=new HttpRequestMessage(HttpMethod.Post, Endpoint(url,route));
        request.Content=new StringContent(JsonSerializer.Serialize(payload,Json),Encoding.UTF8,"application/json");
        if (token is not null) request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancel).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new ServiceFailure($"Service returned HTTP {(int)response.StatusCode}.", route=="/api/sync" && response.StatusCode==HttpStatusCode.Unauthorized);
        if (response.Content.Headers.ContentLength is > 1048576) throw new ServiceFailure("Service response is too large.");
        await using var stream=await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using var buffer=new MemoryStream();
        var chunk=new byte[8192]; int read;
        while ((read=await stream.ReadAsync(chunk,cancel).ConfigureAwait(false))>0)
        { if(buffer.Length+read>1048576) throw new ServiceFailure("Service response is too large."); await buffer.WriteAsync(chunk.AsMemory(0,read),cancel).ConfigureAwait(false); }
        return JsonSerializer.Deserialize<T>(buffer.ToArray(),Json) ?? throw new ServiceFailure("Invalid service response.");
    }
    public Task<PairResult> Pair(string url,string code,CancellationToken cancel) => Post<PairResult>(url,"/api/pair",new { code },null,cancel);
    public Task<SyncResult> Sync(string url,string token,Observation status,List<Choice> events,CancellationToken cancel) => Post<SyncResult>(url,"/api/sync",new { status,events },token,cancel);
    public void Dispose() => http.Dispose();
}
