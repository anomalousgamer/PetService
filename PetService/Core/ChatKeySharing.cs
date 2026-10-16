using System.Text.Json;

namespace PetService.Core;

internal static class ChatKeySharing
{
    internal static bool Verified(ChatVault vault, ChatDevice device) =>
        device.Id == vault.Identity.Id || vault.Pins.GetValueOrDefault(device.Id) == device.PublicKey + "|" + device.SignKey;

    internal static List<ChatQueued> Missing(ChatVault vault, ChatState state,
        Func<ChatThread, string> readVerifiedKey, string? deviceId = null, bool force = false)
    {
        var result = new List<ChatQueued>();
        foreach (var thread in state.Threads.Where(t => !t.Deleted))
        {
            if (vault.Outbox.Any(q => q.Op == "delete" && q.Data.GetProperty("id").GetString() == thread.Id)) continue;
            var devices = state.Devices.Where(d => d.Id != vault.Identity.Id && Verified(vault, d)
                && (deviceId is null || deviceId == d.Id)
                && (force || !thread.Envelopes.Any(e => e.RecipientId == d.Id))
                && !vault.Outbox.Concat(result).Any(q => q.Op == "share"
                    && q.Data.GetProperty("id").GetString() == thread.Id
                    && q.Data.GetProperty("generation").GetInt32() == thread.Generation
                    && q.Data.GetProperty("envelopes").EnumerateArray().Any(e => e.GetProperty("recipientId").GetString() == d.Id))).ToList();
            if (devices.Count == 0) continue;
            string key;
            try { key = readVerifiedKey(thread); } catch { continue; }
            var envelopes = devices.Select(d => PrivateChatCrypto.Wrap(key, thread.Id, thread.Generation, d, vault.Identity)).ToList();
            result.Add(new() { Op = "share", Data = JsonSerializer.SerializeToElement(new
            {
                id = thread.Id, generation = thread.Generation, epoch = vault.Epoch,
                deviceId = vault.Identity.Id, envelopes,
            }, ServiceClient.Json) });
        }
        return result;
    }
}
