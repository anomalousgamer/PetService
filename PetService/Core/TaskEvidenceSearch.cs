using System.Text.Json;

namespace PetService.Core;

internal sealed class TaskEvidenceSearch : IDisposable
{
    private Task<JsonElement>? pending;
    private long revision, pendingRevision;
    private string context="", query="";
    private bool append;
    internal List<JsonElement> Rows {get;private set;}=[];
    internal string Cursor {get;private set;}="";
    internal string Notice {get;private set;}="Enter matching text and select Find observations.";
    internal bool Busy=>pending is not null;
    internal void Select(string pet,string task)
    {
        var next=pet+":"+task;if(next==context)return;
        Reset();context=next;
    }
    internal static string Filter(string kind,string search,string from,string to,string character)
    {
        if(!new[]{"duty","zone","travel","inventory","trade","retainer","job","currency"}.Contains(kind))throw new InvalidOperationException("Choose an event type.");
        if(string.IsNullOrWhiteSpace(search)||search.Trim().Length>200)throw new InvalidOperationException("Enter matching text before searching (up to 200 characters).");
        if(!LocalClock.TryInput(from,out var f)||!LocalClock.TryInput(to,out var t))throw new InvalidOperationException("Enter local times as yyyy-MM-dd HH:mm.");
        if(f is not null&&t is not null&&LocalState.Parse(f)>=LocalState.Parse(t))throw new InvalidOperationException("The end of the period must be after its start.");
        return string.Concat(new[]{("kind",kind),("search",search.Trim()),("from",f??""),("to",t??""),("characterId",character),("timeZone",LocalClock.ZoneId)}.Where(p=>p.Item2.Length>0).Select(p=>"&"+p.Item1+"="+Uri.EscapeDataString(p.Item2)));
    }
    internal void Find(Func<string,Task<JsonElement>> read,string kind,string search,string from,string to,string character,bool older=false)
    {
        if(Busy)return;
        try {
            if(older&&Cursor.Length==0)return;
            var filter=older?query:Filter(kind,search,from,to,character);
            if(!older){Rows=[];Cursor="";}
            pendingRevision=revision;append=older;
            pending=read(filter+(older?"&cursor="+Uri.EscapeDataString(Cursor):""));
            query=filter;Notice="Finding relevant observations…";
        } catch(Exception e){Notice=e.Message;}
    }
    internal void Update()
    {
        if(pending is not {IsCompleted:true})return;
        var task=pending;pending=null;
        try {
            var value=task.GetAwaiter().GetResult();if(pendingRevision!=revision)return;
            var found=PortalJson.Array(value,"records").Select(v=>v.Clone());
            Rows=(append?Rows.Concat(found):found).DistinctBy(v=>PortalJson.Text(v,"key")).ToList();
            Cursor=PortalJson.Text(value,"nextCursor");
            Notice=Rows.Count>0?Rows.Count+" matching observations. Review before attaching.":"No matching observations in this period.";
        } catch {if(pendingRevision==revision)Notice="Could not find observations. Check the period and try again.";}
    }
    private void Reset()
    {
        revision++;if(pending is not null)_=pending.ContinueWith(t=>_=t.Exception,TaskContinuationOptions.OnlyOnFaulted);
        pending=null;Rows=[];Cursor="";query="";Notice="Enter matching text and select Find observations.";
    }
    public void Dispose(){Reset();context="";}
}
