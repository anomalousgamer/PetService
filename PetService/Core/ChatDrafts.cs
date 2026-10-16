namespace PetService.Core;

internal static class ChatDrafts
{
    private static string Key(ChatVault vault, string room) => room + "|" +
        (vault.Personas.FirstOrDefault(p => p.Id == vault.Active)?.Id ?? vault.Personas.FirstOrDefault()?.Id ?? "");
    internal static void Store(ChatVault vault, string room, string text)
    {
        if (room.Length == 0) return;
        vault.Drafts[Key(vault, room)] = text;
        vault.Drafts.Remove(room);
    }
    internal static string Read(ChatVault vault, string room)
    {
        var key = Key(vault, room);
        if (vault.Drafts.TryGetValue(key, out var text)) return text;
        if (!vault.Drafts.Remove(room, out var legacy)) return "";
        vault.Drafts[key] = legacy;
        return legacy;
    }
    internal static void Sent(ChatVault vault, string room) { vault.Drafts.Remove(Key(vault, room)); vault.Drafts.Remove(room); }
    internal static void RemoveRoom(ChatVault vault, string room)
    {
        foreach (var key in vault.Drafts.Keys.Where(k => k == room || k.StartsWith(room + "|", StringComparison.Ordinal)).ToList())
            vault.Drafts.Remove(key);
    }
}
