using System.Text.Json;
namespace PetService.Core;
internal sealed class TaskDraft
{
 internal string Comment="";internal List<bool> Checks=[];
 private string saved="",schema="";private bool submitted;
 private string Stamp()=>JsonSerializer.Serialize(new{Comment,Checks});
 internal bool Dirty=>saved.Length>0&&Stamp()!=saved;
 internal void Receive(PetTask task,bool pending)
 {
  var incomingSchema=JsonSerializer.Serialize(task.Checklist.Select(x=>x.Text));
  if(saved.Length>0&&pending)return;
  if(saved.Length==0||!Dirty||submitted){Comment=task.Comment;Checks=task.Checklist.Select(x=>x.Done).ToList();schema=incomingSchema;saved=Stamp();submitted=false;}
  else if(incomingSchema!=schema){Checks=task.Checklist.Select(x=>x.Done).ToList();schema=incomingSchema;saved=JsonSerializer.Serialize(new{Comment=task.Comment,Checks});}
 }
 internal void Submitted()=>submitted=true;
}
