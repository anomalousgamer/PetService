using System.Numerics;
using Dalamud.Bindings.ImGui;
namespace PetService.Windows;
// Colored text with selection and clipboard support, without HTML or command interpretation.
internal sealed class ChatTranscript
{
 private sealed record Line(string Text,int Offset,int NameEnd,Vector4 Color);
 private List<Line> lines=[];private string full="",key="";private int anchor,caret;private bool selecting,focused;private float width;
 internal void Draw(IReadOnlyList<Core.ChatMessage> messages)
 {
  var current=string.Join('|',messages.Select(m=>m.Id));var available=ImGui.GetContentRegionAvail().X;
  if(key!=current||Math.Abs(width-available)>1){key=current;width=available;Build(messages,Math.Max(1,available));anchor=caret=0;}
  var start=ImGui.GetCursorScreenPos();var height=ImGui.GetTextLineHeightWithSpacing();var total=Math.Max(height,lines.Count*height);
  ImGui.InvisibleButton("##select-transcript",new Vector2(Math.Max(1,available),total));var hovered=ImGui.IsItemHovered();var mouse=ImGui.GetIO().MousePos;
  int Index(){if(lines.Count==0)return 0;var row=Math.Clamp((int)((mouse.Y-start.Y)/height),0,lines.Count-1);var line=lines[row];var x=mouse.X-start.X;var n=0;while(n<line.Text.Length&&ImGui.CalcTextSize(line.Text[..(n+1)]).X<x)n++;return line.Offset+n;}
  if(hovered&&ImGui.IsMouseClicked(ImGuiMouseButton.Left)){focused=true;anchor=caret=Index();selecting=true;}else if(ImGui.IsMouseClicked(ImGuiMouseButton.Left))focused=false;
  if(selecting){caret=Index();if(!ImGui.IsMouseDown(ImGuiMouseButton.Left))selecting=false;}
  var io=ImGui.GetIO();if(focused&&io.KeyCtrl&&ImGui.IsKeyPressed(ImGuiKey.A)){anchor=0;caret=full.Length;}
  if(focused&&io.KeyCtrl&&ImGui.IsKeyPressed(ImGuiKey.C)&&anchor!=caret)ImGui.SetClipboardText(full[Math.Min(anchor,caret)..Math.Max(anchor,caret)]);
  var draw=ImGui.GetWindowDrawList();var a=Math.Min(anchor,caret);var b=Math.Max(anchor,caret);
  for(var n=0;n<lines.Count;n++){
   var line=lines[n];var pos=start+new Vector2(0,n*height);var left=Math.Clamp(a-line.Offset,0,line.Text.Length);var right=Math.Clamp(b-line.Offset,0,line.Text.Length);
   if(right>left)draw.AddRectFilled(pos+new Vector2(ImGui.CalcTextSize(line.Text[..left]).X,0),pos+new Vector2(ImGui.CalcTextSize(line.Text[..right]).X,height),ImGui.GetColorU32(new Vector4(.38f,.30f,.55f,.8f)));
   var split=Math.Clamp(line.NameEnd-line.Offset,0,line.Text.Length);if(split>0)draw.AddText(pos,ImGui.GetColorU32(line.Color),line.Text[..split]);draw.AddText(pos+new Vector2(ImGui.CalcTextSize(line.Text[..split]).X,0),ImGui.GetColorU32(ImGuiCol.Text),line.Text[split..]);
  }
  if(ImGui.BeginPopupContextItem("Transcript copy")){if(ImGui.MenuItem("Copy selected text","",false,a!=b))ImGui.SetClipboardText(full[a..b]);if(ImGui.MenuItem("Copy transcript with names"))ImGui.SetClipboardText(full);ImGui.EndPopup();}
 }
 private void Build(IReadOnlyList<Core.ChatMessage> messages,float width)
 {
  lines=[];var text=new System.Text.StringBuilder();foreach(var message in messages.Where(m=>m.Content is not null)){var c=message.Content!;var prefix="["+c.Alias+"] ";var body=prefix+c.Text;var start=text.Length;var nameEnd=start+prefix.Length;var rgb=0xffffffu;if(uint.TryParse(c.Color.TrimStart('#'),System.Globalization.NumberStyles.HexNumber,null,out var parsed))rgb=parsed;var color=new Vector4(((rgb>>16)&255)/255f,((rgb>>8)&255)/255f,(rgb&255)/255f,1);
   var offset=0;while(offset<body.Length){var newline=body.IndexOf('\n',offset);var end=newline<0?body.Length:newline;while(offset<end){var lo=1;var hi=end-offset;while(lo<hi){var mid=(lo+hi+1)/2;if(ImGui.CalcTextSize(body.Substring(offset,mid)).X<=width)lo=mid;else hi=mid-1;}var length=lo;if(offset+length<end){var space=body.LastIndexOf(' ',offset+length-1,length);if(space>offset)length=space-offset+1;}if(length>1&&char.IsHighSurrogate(body[offset+length-1])&&offset+length<body.Length)length--;lines.Add(new(body.Substring(offset,length),start+offset,nameEnd,color));offset+=length;}if(newline>=0){if(offset==newline)lines.Add(new("",start+offset,nameEnd,color));offset=newline+1;}else break;}text.Append(body).Append("\n\n");lines.Add(new("",text.Length-1,0,color));}full=text.ToString();
 }
}
