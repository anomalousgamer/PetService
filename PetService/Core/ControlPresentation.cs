namespace PetService.Core;
public static class ControlPresentation
{
 public static string Remaining(GameplayLock? value,DateTimeOffset now)
 {
  if(value is null||!value.Enabled)return value?.State=="queued"?"Release queued":"Inactive";
  if(!DateTimeOffset.TryParse(value.ExpiresAtUtc,out var end))return "Until released";
  var seconds=Math.Max(0,(long)Math.Ceiling((end-now).TotalSeconds));
  return seconds==0?"Timer ended":$"{seconds/3600:00}:{seconds%3600/60:00}:{seconds%60:00} remaining";
 }
 public static string Response(ResponseOption o)=>o.Action switch
 {
  "snooze"=>o.Minutes is >0?$"Snoozes for {o.Minutes} minutes; the duration is reported.":"Pet chooses 1–10 minutes; the duration is reported.",
  "random-snooze"=>"Random snooze of 1–10 minutes. The pet sees a neutral confirmation; you receive the actual duration.",
  "complete"=>"Acknowledges and closes this occurrence.",
  "decline"=>"Declines and closes this occurrence.",
  "response"=>o.Resolves?"Reports this response and closes this occurrence.":"Reports this response and leaves this occurrence open.",
  _=>"Unknown action."
 };
}
