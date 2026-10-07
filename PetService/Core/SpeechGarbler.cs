using System.Text;
using System.Text.RegularExpressions;
namespace PetService.Core;

internal static partial class SpeechGarbler
{
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
    internal static string Transform(string text,int strength,string style)
    {
        var syllables=style switch{"soft"=>new[]{'m','n','h'},"playful"=>new[]{'w','a','h'},_=>new[]{'m','p','h'}};
        var output=new StringBuilder(text.Length);var seed=17;
        foreach(var ch in text) {
            // Keep whitespace, punctuation and game placeholders (<t>, <pos>, etc.).
            seed=unchecked(seed*31+ch);
            if(char.IsLetter(ch) && (uint)seed%100<Math.Clamp(strength,1,100)) {
                var replacement=syllables[(uint)seed%3];output.Append(char.IsUpper(ch)?char.ToUpperInvariant(replacement):replacement);
            } else output.Append(ch);
        }
        return output.ToString();
    }
    internal static string Body(string text,int strength,string style)
    {
        var b=new StringBuilder();var offset=0;
        foreach(Match m in Placeholder().Matches(text)){b.Append(Transform(text[offset..m.Index],strength,style));b.Append(m.Value);offset=m.Index+m.Length;}
        b.Append(Transform(text[offset..],strength,style));return b.ToString();
    }
    [GeneratedRegex(@"^(?:[^\s]+\s+[^\s]+@[^\s]+|<(?:[trf2-8]|tell|reply|focus|tt|t2t|me|1)>)\s+",RegexOptions.IgnoreCase)]
    private static partial Regex TellTarget();
    [GeneratedRegex(@"<[^<>\r\n]{1,40}>")]
    private static partial Regex Placeholder();
}
