using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using PetService.Core;

namespace PetService.Windows;

internal sealed class PetCalendarView(Plugin plugin)
{
    private static readonly CultureInfo English=CultureInfo.GetCultureInfo("en-US");
    private DateTime month,selected;
    private string context="",stamp="";
    private long nextRefresh;
    private CalendarMonth calendar=new();
    private readonly Dictionary<DateTime,List<CalendarOccurrence>> days=[];

    internal void Draw()
    {
        var today=plugin.Now.ToLocalTime().Date;
        if(context!=plugin.Configuration.PetId||month==default){context=plugin.Configuration.PetId;month=new(today.Year,today.Month,1);selected=today;stamp="";}
        Style.Title("Your calendar","Read-only · dates and times on your local clock");
        var scale=ImGuiHelpers.GlobalScale;
        ImGui.BeginDisabled(month.Year<=2);if(ImGui.Button("Previous"))Move(-1);ImGui.EndDisabled();ImGui.SameLine();
        if(ImGui.Button("Today")){month=new(today.Year,today.Month,1);selected=today;stamp="";}ImGui.SameLine();
        ImGui.BeginDisabled(month.Year>=9998);if(ImGui.Button("Next"))Move(1);ImGui.EndDisabled();
        ImGui.Spacing();ImGui.SetWindowFontScale(1.2f);ImGui.TextColored(Style.Accent,month.ToString("MMMM yyyy",English));ImGui.SetWindowFontScale(1);
        Refresh();
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding,new Vector2(4,4)*scale);
        if(ImGui.BeginTable("pet-calendar-month",7,ImGuiTableFlags.SizingStretchSame)){
            for(var column=0;column<7;column++){ImGui.TableNextColumn();ImGui.TextColored(Style.Muted,new[]{"Sun","Mon","Tue","Wed","Thu","Fri","Sat"}[column]);}
            var first=month.AddDays(-(int)month.DayOfWeek);
            for(var n=0;n<42;n++){
                ImGui.TableNextColumn();var day=first.AddDays(n);var inMonth=day.Month==month.Month;var chosen=day==selected;
                days.TryGetValue(day,out var events);var count=events?.Count??0;
                ImGui.PushID(day.ToString("yyyyMMdd",CultureInfo.InvariantCulture));
                if(chosen){ImGui.PushStyleColor(ImGuiCol.Button,Style.Accent);ImGui.PushStyleColor(ImGuiCol.ButtonHovered,Style.Accent);}
                ImGui.BeginDisabled(!inMonth);
                var start=ImGui.GetCursorScreenPos();var width=Math.Max(1,ImGui.GetContentRegionAvail().X);var height=58*scale;
                if(ImGui.Button("##date",new Vector2(width,height)))selected=day;
                var draw=ImGui.GetWindowDrawList();var color=ImGui.GetColorU32(chosen?Style.AccentText:inMonth?Vector4.One:Style.Muted);
                draw.AddText(start+new Vector2(8,6)*scale,color,day.Day.ToString(CultureInfo.InvariantCulture));
                if(count>0)draw.AddText(start+new Vector2(8,32)*scale,color,count==1?"1 item":count+" items");
                if(day==today)draw.AddRect(start+new Vector2(1),start+new Vector2(width-1,height-1),ImGui.GetColorU32(chosen?Style.AccentText:Style.Accent),6,ImDrawFlags.None,2);
                if(inMonth&&ImGui.IsItemHovered()&&events is {Count:>0}){
                    ImGui.BeginTooltip();foreach(var entry in events.Take(4))ImGui.TextUnformatted(Time(entry)+" · "+entry.Label);if(count>4)ImGui.TextUnformatted("+ "+(count-4)+" more");ImGui.EndTooltip();
                }
                ImGui.EndDisabled();if(chosen)ImGui.PopStyleColor(2);ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();ImGui.Spacing();
        Style.Title(selected.ToString("dddd, MMMM d",English),"Scheduled routines and reminders");
        if(days.TryGetValue(selected,out var entries))foreach(var entry in entries){
            Style.BeginCard("calendar-"+entry.Id,Time(entry)+" · "+entry.Label,entry.Kind);
            ImGui.TextColored(entry.State=="Completed"?Style.Good:Style.Muted,entry.State);Style.EndCard();
        }else ImGui.TextColored(Style.Muted,"No routines or reminders scheduled for this day.");
        if(calendar.Unavailable.Count>0)ImGui.TextWrapped("Some cached schedules could not be shown. Reconnect to refresh them.");
        ImGui.TextWrapped("This calendar shows your saved schedule. Configured deferrals can delay when a reminder appears. Respond to reminders on Routines or in their popup.");
        if(plugin.KillSwitchOn)ImGui.TextColored(Style.Muted,"Paused · showing your last saved schedule.");
    }
    private static string Time(CalendarOccurrence entry)=>entry.AtUtc.ToLocalTime().ToString("h:mm tt",English);
    private void Move(int amount){month=month.AddMonths(amount);selected=month;stamp="";}
    private void Refresh()
    {
        var value=plugin.Configuration.LastSyncAtUtc??"";
        var key=plugin.Configuration.V6Outbox.Count+"|"+plugin.Configuration.Outbox.Count+"|"+context+"|"+month.ToString("yyyy-MM",CultureInfo.InvariantCulture)+"|"+value+"|"+TimeZoneInfo.Local.Id;
        if(key==stamp&&Environment.TickCount64<nextRefresh)return;
        calendar=RoutineCalendar.Build(plugin.Configuration.Features.V6.Routines,plugin.Configuration.Reminders,
            plugin.Configuration.Features.V6.Prompts,plugin.Configuration.Prompts,month,TimeZoneInfo.Local,plugin.Now);
        days.Clear();foreach(var group in calendar.Entries.GroupBy(e=>e.AtUtc.ToLocalTime().Date))days[group.Key]=group.ToList();
        stamp=key;nextRefresh=Environment.TickCount64+60000;
    }
}
