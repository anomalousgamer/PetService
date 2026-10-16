namespace PetService.Core;

public sealed record ChatAlert(string ConversationId, bool NewConversation);

internal static class ChatActivity
{
    internal static List<ChatAlert> Observe(ChatVault vault, ChatState state, string viewingRoom, ChatPreferences? preferences = null)
    {
        var alerts = new List<ChatAlert>();
        var first = !vault.NotificationsInitialized && vault.Notified.Count == 0; var prefs = preferences ?? state.Preferences;
        foreach (var thread in state.Threads)
        {
            if (thread.Deleted) { vault.Unread.Remove(thread.Id); vault.Notified.Remove(thread.Id); continue; }
            var known = vault.Notified.TryGetValue(thread.Id, out var previous);
            if (first) { vault.Notified[thread.Id] = thread.LastPeerSequence; continue; }
            if (prefs.GlobalPaused || prefs.Paused.Contains(thread.Id)) continue;
            var fromPeer = state.Devices.Any(d => d.Id == thread.Title.DeviceId && d.Role != vault.Role);
            var created = !known && fromPeer;
            var message = ulong.TryParse(thread.LastPeerSequence, out var latest) && latest >
                (ulong.TryParse(previous, out var old) ? old : 0);
            if ((created || message) && thread.Id != viewingRoom)
            {
                vault.Unread.Add(thread.Id);
                if (!prefs.Muted.Contains(thread.Id)) alerts.Add(new(thread.Id, created));
            }
            vault.Notified[thread.Id] = thread.LastPeerSequence;
        }
        vault.NotificationsInitialized = true;
        return alerts;
    }
}
