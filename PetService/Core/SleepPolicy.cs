namespace PetService.Core;

public static class SleepPolicy
{
    public static bool Requested(SleepControl? command,IEnumerable<string> ended,string characterId)=>
        command is {Enabled:true} && command.CharacterId==characterId && !ended.Contains(command.Id);
    public static void Remember(List<string> ended,string id){if(!ended.Contains(id))ended.Add(id);}
}
public sealed class SleepControl
{
    public string Id{get;set;}="";
    public string CharacterId{get;set;}="";
    public bool Enabled{get;set;}
    public string State{get;set;}="queued";
    public string RequestedAtUtc{get;set;}="";
    public string Detail{get;set;}="";
    public string Operation{get;set;}="sleep";
}
