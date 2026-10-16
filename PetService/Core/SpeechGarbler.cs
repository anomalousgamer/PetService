using System.Text;
using System.Text.RegularExpressions;
namespace PetService.Core;

internal static partial class SpeechGarbler
{
    internal static string Channel(string text)
    {
        var command=text.TrimStart().Split(' ',2)[0].ToLowerInvariant();
        return command switch {
            "/s" or "/say"=>"say", "/t" or "/tell" or "/r" or "/reply"=>"tell", "/p" or "/party"=>"party", "/a" or "/alliance"=>"alliance", "/fc" or "/freecompany"=>"freecompany", "/y" or "/yell"=>"yell", "/sh" or "/shout"=>"shout", "/n" or "/novice"=>"novice", "/pt" or "/pvpteam"=>"pvpteam", "/e" or "/echo"=>"echo", _=>command.StartsWith("/cwlinkshell")||command.StartsWith("/cwl")?"cwlinkshell":command.StartsWith("/linkshell")||System.Text.RegularExpressions.Regex.IsMatch(command,@"^/l[1-8]?$")?"linkshell":"default"
        };
    }
    internal static bool Excluded(string text,IReadOnlyCollection<string> excluded,int linkshell=0,int crossworld=0,string currentChannel="")
    {
        var command=text.TrimStart().Split((char[]?)null,2,StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant()??"";
        var channel=Channel(text);
        if(!command.StartsWith('/')) {
            var current=currentChannel.Trim().ToLowerInvariant();
            if(!current.StartsWith('/'))current="/"+current;
            if(Regex.IsMatch(current,@"^/(?:s|say|t|tell|p|party|a|alliance|fc|freecompany|y|yell|sh|shout|n|novice|pt|pvpteam|l[1-8]?|linkshell[1-8]?|cwl[1-8]?|cwlinkshell[1-8]?)$")) {command=current;channel=Channel(current);}
        }
        if(excluded.Contains(channel))return true;
        var match=Regex.Match(command,@"^/(cwl|cwlinkshell|l|linkshell)([1-8]?)$",RegexOptions.CultureInvariant);
        if(!match.Success)return false;
        var cross=match.Groups[1].Value is "cwl" or "cwlinkshell";
        var slot=match.Groups[2].Success&&match.Groups[2].Length>0?match.Groups[2].Value[0]-'0':cross?crossworld:linkshell;
        return slot is >=1 and <=8&&excluded.Contains((cross?"cwlinkshell":"linkshell")+slot);
    }
    // Work only on the message body; destination names never leave this method.
    internal static int BodyStart(string text)
    {
        var start=0;while(start<text.Length && char.IsWhiteSpace(text[start]))start++;
        if(start==text.Length)return -1;
        if(text[start]!='/')return start;
        var end=start;while(end<text.Length && !char.IsWhiteSpace(text[end]))end++;
        var cmd=text[start..end].ToLowerInvariant();
        if(cmd is "/petservice" or "/toh" || !ChatCommandPolicy.Allows(text))return -1;
        while(end<text.Length && char.IsWhiteSpace(text[end]))end++;
        if(cmd is "/t" or "/tell") {
            // Explicit tells require a two-part name + world, or a target placeholder.
            var match=TellTarget().Match(text[end..]);if(!match.Success)return -1;end+=match.Length;
        }
        return end<text.Length?end:-1;
    }
    internal static string Transform(string text,int strength,string style,string custom="")
    {
        var syllables=style=="custom"&&custom.Length>0?custom.ToCharArray():style switch{"soft"=>new[]{'m','n','h'},"playful"=>new[]{'w','a','h'},_=>new[]{'m','p','h'}};
        var output=new StringBuilder(text.Length);var seed=17;
        foreach(var ch in text) {
            // Keep whitespace, punctuation and game placeholders (<t>, <pos>, etc.).
            seed=unchecked(seed*31+ch);
            if(char.IsLetter(ch) && (uint)seed%100<Math.Clamp(strength,1,100)) {
                var replacement=syllables[(uint)seed%syllables.Length];output.Append(char.IsUpper(ch)?char.ToUpperInvariant(replacement):replacement);
            } else output.Append(ch);
        }
        return output.ToString();
    }
    internal static string Body(string text,int strength,string style,string custom="")
    {
        var b=new StringBuilder();var offset=0;
        foreach(Match m in Placeholder().Matches(text)){b.Append(Transform(text[offset..m.Index],strength,style,custom));b.Append(m.Value);offset=m.Index+m.Length;}
        b.Append(Transform(text[offset..],strength,style,custom));return b.ToString();
    }
    [GeneratedRegex(@"^(?:[^\s]+\s+[^\s]+@[^\s]+|<(?:[trf2-8]|tell|reply|focus|tt|t2t|me|1)>)\s+",RegexOptions.IgnoreCase)]
    private static partial Regex TellTarget();
    [GeneratedRegex(@"<[^<>\r\n]{1,40}>")]
    private static partial Regex Placeholder();
}
