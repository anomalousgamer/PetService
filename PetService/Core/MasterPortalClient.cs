using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PetService.Core;

internal sealed class AdminFailure(string code,bool unauthorized=false) : Exception(code)
{
    internal bool Unauthorized { get; }=unauthorized;
}
internal sealed class MasterPortalClient : IDisposable
{
    private readonly HttpClient http;
    internal MasterPortalClient(HttpMessageHandler? handler=null)
    {
        http=new(handler ?? new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(12)};
    }
    internal Task<AdminDashboard> Dashboard(string password,string pet,CancellationToken cancel)=>Request<AdminDashboard>(
        "/api/admin/dashboard"+(pet.Length==0 ? "" : "?pet="+Uri.EscapeDataString(pet)),password,null,cancel);
    internal Task<AdminActionResult> Action(string password,AdminAction action,CancellationToken cancel)=>Request<AdminActionResult>(
        "/api/admin/action",password,action,cancel);
    private async Task<T> Request<T>(string route,string password,object? payload,CancellationToken cancel)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));cancel=deadline.Token;
        using var request=new HttpRequestMessage(payload is null ? HttpMethod.Get : HttpMethod.Post,ServiceClient.Endpoint(ServiceClient.BaseUrl,route));
        request.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:"+password)));
        if(payload is not null)request.Content=new StringContent(JsonSerializer.Serialize(payload,ServiceClient.Json),Encoding.UTF8,"application/json");
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancel).ConfigureAwait(false);
        if(response.Content.Headers.ContentLength is >1048576)throw new AdminFailure("RESPONSE_TOO_LARGE");
        await using var stream=await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using var buffer=new MemoryStream();var chunk=new byte[8192];int read;
        while((read=await stream.ReadAsync(chunk,cancel).ConfigureAwait(false))>0) {
            if(buffer.Length+read>1048576)throw new AdminFailure("RESPONSE_TOO_LARGE");
            await buffer.WriteAsync(chunk.AsMemory(0,read),cancel).ConfigureAwait(false);
        }
        if(!response.IsSuccessStatusCode) {
            string code="SERVICE_UNAVAILABLE";
            try {
                using var error=JsonDocument.Parse(buffer.ToArray());
                var value=error.RootElement.GetProperty("code").GetString();
                if(value is not null && System.Text.RegularExpressions.Regex.IsMatch(value,@"\A[A-Z][A-Z0-9_]{2,70}\z"))code=value;
            } catch { }
            throw new AdminFailure(code,response.StatusCode==HttpStatusCode.Unauthorized);
        }
        return JsonSerializer.Deserialize<T>(buffer.ToArray(),ServiceClient.Json) ?? throw new AdminFailure("INVALID_RESPONSE");
    }
    public void Dispose()=>http.Dispose();
}
