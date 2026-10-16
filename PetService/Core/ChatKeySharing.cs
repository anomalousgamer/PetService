using System.Text.Json;

namespace PetService.Core;

internal static class ChatKeySharing
{
    // Enrollment is authenticated by the existing Pet/Master pairing over HTTPS.
    // Remember each device's first public keys; a later key substitution is not accepted.
    internal static int RememberPairedDevices(ChatVault vault, ChatState state)
    {
        if (state.PetId != vault.Scope || state.Role != vault.Role)
            throw new InvalidOperationException("Chat response belongs to another profile.");
        if (state.Devices.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != state.Devices.Count)
            throw new InvalidOperationException("The chat service returned duplicate devices.");
        var added = 0;
        foreach (var device in state.Devices)
        {
            if (device.Role is not ("pet" or "master") || !Guid.TryParse(device.Id, out _)
                || string.IsNullOrWhiteSpace(device.PublicKey) || string.IsNullOrWhiteSpace(device.SignKey))
                throw new InvalidOperationException("The chat service returned an invalid device.");
            if (device.Id == vault.Identity.Id)
            {
                if (device.Role != vault.Role || device.PublicKey != vault.Identity.PublicKey || device.SignKey != vault.Identity.SignKey)
                    throw new InvalidOperationException("This chat device's saved keys do not match its enrollment.");
                continue;
            }
            if (vault.Pins.TryAdd(device.Id, device.PublicKey + "|" + device.SignKey)) added++;
        }
        return added;
    }

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
