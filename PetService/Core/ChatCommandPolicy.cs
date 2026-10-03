namespace PetService.Core;

internal static class ChatCommandPolicy
{
    private static readonly HashSet<string> ChatCommands = BuildCommands();

    public static bool Allows(string text)
    {
        // A pasted second line must not become a second command.
        if (text.Contains('\r') || text.Contains('\n') || text.Contains('\0'))
            return false;
        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith('/'))
            return true;

        var end = 1;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]))
            end++;
        return ChatCommands.Contains(trimmed[..end]);
    }

    private static HashSet<string> BuildCommands()
    {
        // Exact English text-command names and aliases from the Lodestone.
        var commands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/say", "/s", "/yell", "/y", "/shout", "/sh", "/tell", "/t",
            "/reply", "/r", "/party", "/p", "/alliance", "/a",
            "/freecompany", "/fc", "/pvpteam", "/pt", "/novice", "/n",
            "/echo", "/e", "/petservice", "/linkshell", "/l", "/cwlinkshell", "/cwl",
        };
        for (var i = 1; i <= 8; i++)
        {
            commands.Add($"/linkshell{i}");
            commands.Add($"/l{i}");
            commands.Add($"/cwlinkshell{i}");
            commands.Add($"/cwl{i}");
        }
        return commands;
    }
}
