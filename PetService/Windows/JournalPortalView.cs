using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using PetService.Core;

namespace PetService.Windows;

internal sealed class JournalPortalView:IDisposable
{
    private string from=DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd HH:mm"),to="",kind="",search="",world="",dc="",duty="",item="",character="",preset="";
    private string context="",cursor="",error="",loadedQuery="";
    private Task<JsonElement>? pending;
    private Task<JsonElement>? export;
    private bool append,statistics;
    private long requestRevision;
    private readonly List<JsonElement> rows=[];
    private JsonElement result;
    internal bool EditingText{get;private set;}
    private static string Text(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind is not(JsonValueKind.Null or JsonValueKind.Undefined)?v.ToString():"";
    private static double Number(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.TryGetDouble(out var n)?n:0;
    private static string Local(string s)=>PortalPresentation.Date(s);
    private static string Duration(double n)=>TimeSpan.FromSeconds(Math.Max(0,n)).ToString(@"d\d\ hh\h\ mm\m");
    private static IEnumerable<JsonElement> Array(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(key,out var a)&&a.ValueKind==JsonValueKind.Array?a.EnumerateArray():[];
    private void Input(string label,ref string value,int limit=200){ImGui.SetNextItemWidth(-1);ImGui.InputText(label,ref value,limit);EditingText|=ImGui.IsItemActive();}
    private string Query(bool stats,bool older=false)
    {
        if(!LocalClock.TryInput(from,out var f)||!LocalClock.TryInput(to,out var t))throw new InvalidOperationException("Enter local times as yyyy-MM-dd HH:mm.");
        if(f is not null&&t is not null&&LocalState.Parse(f)>=LocalState.Parse(t))throw new InvalidOperationException("The end of the period must be after its start.");
        var pairs=new List<(string,string)>{("from",f??""),("to",t??""),("timeZone",LocalClock.ZoneId),("characterId",character)};
        if(!stats)pairs.AddRange(new[]{("kind",kind),("search",search),("world",world),("dataCenter",dc),("duty",duty),("item",item)});
        if(older)pairs.Add(("cursor",cursor));
        return string.Concat(pairs.Where(p=>p.Item2.Length>0).Select(p=>"&"+p.Item1+"="+Uri.EscapeDataString(p.Item2)));
    }
    private void Load(MasterPortalSession session,bool stats,bool older=false)
    {
        if(pending is not null)return;
        try{requestRevision=session.JournalRevision;append=older;statistics=stats;var query=older?loadedQuery+"&cursor="+Uri.EscapeDataString(cursor):Query(stats);pending=session.ReadJournal(query,stats);if(!older)loadedQuery=query;error="";}catch(Exception e){error=e.Message;}
    }
    internal void Draw(MasterPortalSession session,AdminPet pet,string page)
    {
        EditingText=false;
        var changed=context!=pet.Name+":"+page;
        if(changed){Observe(pending);pending=null;Observe(export);export=null;rows.Clear();cursor="";result=default;context=pet.Name+":"+page;error="";summaryLimits.Clear();kind=page switch{"Travel"=>"travel","Inventory history"=>"inventory","Retainers"=>"retainer",_=>""};if(!pet.Features.Characters.Any(c=>c.Id==character))character="";}
        var stats=page is not("Journal" or "Travel" or "Inventory history" or "Retainers");
        if(pending is {IsCompleted:true}) {
            var task=pending;pending=null;
            try{var value=task.GetAwaiter().GetResult();if(requestRevision==session.JournalRevision){result=value;if(!statistics){if(!append)rows.Clear();rows.AddRange(Array(value,"records").Select(x=>x.Clone()));cursor=Text(value,"nextCursor");}else summaryLimits.Clear();}}
            catch{error="Could not load saved observations. Check the date range and retry (statistics allow up to one year).";}
        }
        Style.Title(page,page=="Journal"?"Saved event observations and Pet Service outcomes":stats?"Statistics from saved observations":page=="Inventory history"?"Observed inventory changes; their source may be unconfirmed":page=="Retainers"?"Observed venture result screens; viewing does not confirm collection":"Completed arrivals, including same-zone transfers. Ordinary walking is not recorded as a trip.");
        Style.BeginCard("journal-filters","Period and filters","Enter and view times on your local clock");
        if(ImGui.BeginTable("dates",2,ImGuiTableFlags.SizingStretchSame)) {
            ImGui.TableNextColumn();Input("From (yyyy-MM-dd HH:mm)",ref from,24);
            ImGui.TableNextColumn();Input("Before (blank = now)",ref to,24);ImGui.EndTable();
        }
        if(Style.BeginCombo("Character",pet.Features.Characters.FirstOrDefault(c=>c.Id==character)?.Name??"All approved characters")) {
            if(ImGui.Selectable("All approved characters",character.Length==0))character="";
            foreach(var c in pet.Features.Characters.Where(c=>c.Allowed))if(ImGui.Selectable(c.Name+" @ "+c.HomeWorld,c.Id==character))character=c.Id;ImGui.EndCombo();
        }
        if(!stats) {
            Input("Search saved observations",ref search);
            if(ImGui.CollapsingHeader("More event filters")) {
                if(page=="Journal"&&Style.BeginCombo("Event type",kind.Length==0?"All events":ActivityLabels.Kind(kind))) {
                    foreach(var k in new[]{"","session","zone","state","job","duty","inventory","trade","retainer","travel","gap","response","bond","sleep","honorific","moodles","task","currency","combat"})if(ImGui.Selectable(k.Length==0?"All events":ActivityLabels.Kind(k),k==kind))kind=k;
                    ImGui.EndCombo();
                }
                Input("World",ref world);Input("Data center",ref dc);Input("Duty",ref duty);Input("Item",ref item);
            }
        }
        ImGui.BeginDisabled(pending is not null);
        if(Style.PrimaryButton(stats?"Load statistics":"Load saved events",new Vector2(-1,40)))Load(session,stats);
        ImGui.EndDisabled();Style.EndCard();
        if(!stats&&ImGui.CollapsingHeader("Saved filters and CSV export")) {
            if(ImGui.Button("Export displayed CSV"))try{SaveCsv(rows,pet.Name);}catch{error="Could not save CSV. Check configuration folder permissions.";}
            Input("Filter name",ref preset,60);
            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(preset));
            if(ImGui.Button("Save filter"))try{session.Action(new(){Action="journalPreset",Pet=pet.Name,Data=new{name=preset,filter=Filter()}});}catch(Exception e){error=e.Message;}
            ImGui.SameLine();if(ImGui.Button("Delete filter"))session.Action(new(){Action="journalPreset",Pet=pet.Name,Data=new{name=preset,remove=true}});ImGui.EndDisabled();
            foreach(var p in Array(result,"presets")) {
                ImGui.PushID(Text(p,"name"));
                if(Style.LiteralButton(Text(p,"name"),"preset",new Vector2(-1,32))&&p.TryGetProperty("filter",out var f)) {
                    preset=Text(p,"name");from=LocalClock.Input(Text(f,"from"));to=LocalClock.Input(Text(f,"to"));kind=Text(f,"kind");search=Text(f,"search");world=Text(f,"world");dc=Text(f,"dataCenter");duty=Text(f,"duty");item=Text(f,"item");character=Text(f,"characterId");Load(session,false);
                }
                ImGui.PopID();
            }
        }
        if(changed&&!ImGui.GetIO().AppFocusLost)Load(session,stats);
        if(pending is not null)ImGui.TextColored(Style.Muted,"Loading saved observations…");if(error.Length>0)ImGui.TextWrapped(error);
        if(stats){if(result.ValueKind==JsonValueKind.Object)DrawSummary(result,pet,page);return;}
        ImGui.TextColored(Style.Muted,rows.Count+" displayed events"+(cursor.Length>0?" · Older records available":""));
        foreach(var record in rows)JournalRecordView.Draw(record);
        if(rows.Count==0&&pending is null)ImGui.TextColored(Style.Muted,"No matching event observations in this period.");
        ImGui.BeginDisabled(pending is not null||cursor.Length==0);if(cursor.Length>0&&ImGui.Button("Load older events"))Load(session,false,true);ImGui.EndDisabled();
    }
    private Dictionary<string,string> Filter()=>Query(false).TrimStart('&').Split('&',StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Split('=',2)).ToDictionary(x=>x[0],x=>Uri.UnescapeDataString(x[1]));
    private static void SaveCsv(IEnumerable<JsonElement> records,string pet)
    {
        static string Cell(string v)=>"\""+((v.Length>0&&"=+-@".Contains(v[0]))?"'":"")+v.Replace("\"","\"\"")+"\"";
        var lines=new List<string>{"Time (local),Type,Character,Home world,Details"};foreach(var e in records){var detail=e.TryGetProperty("data",out var data)?LocalClock.ExportDetails(data):"";lines.Add(string.Join(',',new[]{LocalClock.Export(Text(e,"atUtc")),Text(e,"kind"),Text(e,"characterName"),Text(e,"homeWorld"),detail}.Select(Cell)));}
        var dir=Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(),"exports");Directory.CreateDirectory(dir);var file=Path.Combine(dir,pet+"-journal-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv");File.WriteAllLines(file,lines,new UTF8Encoding(true));ImGui.SetClipboardText(file);
    }
    private readonly Dictionary<string,int> summaryLimits=[];
    private void SummaryTable(string id,string title,IEnumerable<JsonElement> source,params (string Label,Func<JsonElement,string> Value)[] columns)
    {
        var all=source.ToList();Style.BeginCard(id,title,all.Count+" observations in this period");
        var limit=summaryLimits.GetValueOrDefault(id,25);
        if(all.Count>0&&ImGui.BeginTable("rows",columns.Length,ImGuiTableFlags.SizingStretchSame|ImGuiTableFlags.RowBg|ImGuiTableFlags.BordersInnerH)) {
            foreach(var column in columns)ImGui.TableSetupColumn(column.Label);ImGui.TableHeadersRow();
            foreach(var row in all.Take(limit)){ImGui.TableNextRow();foreach(var column in columns){ImGui.TableNextColumn();ImGui.TextWrapped(column.Value(row));}}
            ImGui.EndTable();
        }
        if(all.Count==0)ImGui.TextColored(Style.Muted,"No observations in this period.");
        if(all.Count>limit&&ImGui.Button("Show 25 more"))summaryLimits[id]=limit+25;
        Style.EndCard();
    }
    private void DrawSummary(JsonElement s,AdminPet pet,string page)
    {
        ImGui.TextWrapped(Local(Text(s,"fromUtc"))+" – "+Local(Text(s,"toUtc"))+" · "+Number(s,"gaps")+" recorded gaps");
        if(PortalJson.Bool(s,"eventLimitReached"))ImGui.TextWrapped("Event summaries show the latest 5,000 events. Interval totals include all matching observations.");
        string Character(JsonElement row){var id=Text(row,"characterId");return pet.Features.Characters.FirstOrDefault(c=>c.Id==id)?.Name??Text(row,"characterName");}
        if(page=="Analytics") {
            Style.BeginCard("analytics-total","Observed playtime","Saved intervals exclude missing observations and local pauses");
            Style.Metric("Total observed",Duration(Number(s,"totalSeconds")),"total");ImGui.TextWrapped($"{Number(s,"sessionCount")} sessions · Average {Duration(Number(s,"averageSessionSeconds"))} · Longest {Duration(Number(s,"longestSessionSeconds"))}");
            Style.DailyChart(Array(s,"daily").Select(d=>new DailyTime{Day=Text(d,"day"),Seconds=Number(d,"seconds")}).ToArray());Style.EndCard();
            foreach(var (label,key) in new[]{("By job","jobs"),("By location","locations"),("By character","characters")})SummaryTable(key,label,Array(s,key),("Name",row=>pet.Features.Characters.FirstOrDefault(c=>c.Id==Text(row,"label"))?.Name??Text(row,"label")),("Observed time",row=>Duration(Number(row,"seconds"))));
            var state=PortalJson.Get(s,"states");
            var states=state.ValueKind==JsonValueKind.Object?state.EnumerateObject().Select(p=>JsonSerializer.SerializeToElement(new{label=p.Name,seconds=p.Value})):[];
            SummaryTable("game-states","Observed game state",states,("State",r=>ActivityLabels.Field(Text(r,"label").Replace("Seconds",""))),("Time",r=>Duration(Number(r,"seconds"))));
            SummaryTable("sessions","Sessions",Array(s,"sessions"),("Character",r=>Text(r,"characterName")),("First observed",r=>Local(Text(r,"firstObservedAtUtc"))),("Last observed",r=>Local(Text(r,"lastObservedAtUtc"))),("Observed time",r=>Duration(Number(r,"seconds"))));
            var travel=PortalJson.Get(s,"travel");
            foreach(var key in new[]{"methods","destinations"}) {
                var groups=PortalJson.Get(travel,key);var data=groups.ValueKind==JsonValueKind.Object?groups.EnumerateObject().Select(p=>JsonSerializer.SerializeToElement(new{label=p.Name,count=p.Value})):[];
                SummaryTable("travel-"+key,key=="methods"?"Travel methods":"Travel destinations",data,("Name",r=>key=="methods"?ActivityLabels.TravelMethod(Text(r,"label")):Text(r,"label")),("Trips",r=>Text(r,"count")));
            }
            if(ImGui.CollapsingHeader("Travel event coordinates (no walking trail)"))SummaryTable("travel-markers","Travel coordinates",Array(travel,"markers"),("Time",r=>Local(Text(r,"atUtc"))),("Location",r=>Text(r,"zone")),("Coordinates",r=>PortalPresentation.Coordinates(r,"x","y")));
            var inventory=PortalJson.Get(s,"inventory");SummaryTable("inventory-ledger","Inventory ledger totals (source unconfirmed)",Array(inventory,"items"),("Item",r=>Text(r,"name")+(PortalJson.Bool(r,"hq")?" HQ":"")),("Added",r=>Text(r,"added")),("Removed",r=>Text(r,"removed")),("Transfers",r=>Text(r,"moved")));
        }
        if(page=="Zone visits")SummaryTable("visits","Zone visits",Array(s,"visits"),("Character / location",r=>Character(r)+" · "+Text(r,"world")+" · "+Text(r,"zone")),("Arrived / departed",r=>Local(Text(r,"arrivedAtUtc"))+" – "+Local(Text(r,"departedAtUtc"))),("Wall duration",r=>PortalJson.Bool(r,"interrupted")?"Interrupted":PortalJson.Get(r,"wallSeconds").ValueKind==JsonValueKind.Number?Duration(Number(r,"wallSeconds")):"Unknown"),("Coordinates",r=>PortalPresentation.Coordinates(r,"x","y")));
        if(page=="Duty summaries")SummaryTable("duties","Duty runs",Array(s,"duties"),("Duty / outcome",r=>Text(r,"name")+" · "+(PortalJson.Bool(r,"completed")?"Completed":PortalPresentation.State(Text(r,"outcome")))),("Started / ended",r=>Local(Text(r,"startedAtUtc"))+" – "+Local(Text(r,"endedAtUtc"))),("Duration",r=>PortalJson.Get(r,"seconds").ValueKind==JsonValueKind.Number?Duration(Number(r,"seconds")):"Unknown"),("Wipes / recommences",r=>Text(r,"wipes")+" / "+Text(r,"recommences")));
        if(page=="Trade summary") {
            var trades=PortalJson.Get(s,"trades");Style.BeginCard("trade-total","Trade totals","");
            ImGui.TextWrapped(Number(trades,"completed")+" completed · "+Number(trades,"cancelled")+" canceled · "+Number(trades,"unknown")+" failed, interrupted, or unconfirmed");
            ImGui.TextWrapped("Gave "+PortalPresentation.Count(Number(trades,"giveGil"))+" gil · Received "+PortalPresentation.Count(Number(trades,"receiveGil"))+" gil");Style.EndCard();
            SummaryTable("trade-items","Traded items",Array(trades,"items"),("Side",r=>PortalPresentation.State(Text(r,"side"))),("Item",r=>Text(r,"name")+(PortalJson.Bool(r,"hq")?" HQ":"")),("Quantity",r=>Text(r,"quantity")));
        }
        if(page=="Bonding history") {
            var state=PortalJson.Get(s,"states");Style.BeginCard("blocking-total","Observed blocking time","Overlapping reasons are counted once in the total");Style.Metric("Actually blocked",Duration(Number(state,"blockedSeconds")),"blocked");
            foreach(var key in new[]{"bondedSeconds","sleepSeconds","promptSeconds"})ImGui.TextWrapped(ActivityLabels.Field(key.Replace("Seconds",""))+" · "+Duration(Number(state,key)));
            ImGui.TextWrapped(Number(s,"guardIntervals")+" intervals include blocking observations. Earlier releases did not record these flags; gaps are excluded. Use Journal for requested, applied, and released outcomes.");Style.EndCard();
        }
    }
    private static void Observe(Task? task){if(task is not null)_=task.ContinueWith(t=>_=t.Exception,TaskContinuationOptions.OnlyOnFaulted);}
    public void Dispose(){Observe(pending);Observe(export);pending=null;export=null;rows.Clear();result=default;context="";cursor="";error="";}
}
