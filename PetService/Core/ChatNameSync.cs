using System.Text.Json;
using System.Text.RegularExpressions;

namespace PetService.Core;

public sealed class ChatNameLibrary
{
    public string Id { get; set; } = "";
    public int Revision { get; set; }
    public ChatPacket Packet { get; set; } = new();
    public List<ChatEnvelope> Envelopes { get; set; } = [];
}
public sealed class ChatNameEdit
{
    public string ChangeId { get; set; } = Guid.NewGuid().ToString();
    public ChatPersona Persona { get; set; } = new();
    public bool Deleted { get; set; }
    public bool AddOnly { get; set; }
    public bool NameChanged { get; set; }
    public bool ColorChanged { get; set; }
    public bool Create { get; set; }
}
public sealed class ChatNameSyncState
{
    public bool Migrated { get; set; }
    public string Id { get; set; } = "";
    public string Key { get; set; } = "";
    public int Revision { get; set; }
    public List<ChatPersona> Base { get; set; } = [];
    public List<ChatNameEdit> Edits { get; set; } = [];
}
public sealed class ChatNameContent { public List<ChatPersona> Personas { get; set; } = []; }

internal static class ChatNameSync
{
    private static ChatPersona Copy(ChatPersona p) => new() { Id = p.Id, Name = p.Name, Color = p.Color.ToLowerInvariant() };
    private static bool Same(ChatPersona a, ChatPersona b) => a.Name == b.Name && a.Color.Equals(b.Color, StringComparison.OrdinalIgnoreCase);
    private static void Validate(List<ChatPersona> values)
    {
        if (values.Count is < 1 or > 40 || values.Select(p => p.Id).Distinct().Count() != values.Count
            || values.Any(p => !Guid.TryParseExact(p.Id, "D", out _) || p.Name.Length > 60 || !Regex.IsMatch(p.Color, "^#[0-9a-fA-F]{6}$")))
            throw new InvalidOperationException("Speaking names must contain between 1 and 40 valid names and colors.");
    }
    private static List<ChatPersona> Project(ChatNameSyncState sync)
    {
        var values = sync.Base.Select(Copy).ToList();
        foreach (var edit in sync.Edits)
        {
            var i = values.FindIndex(p => p.Id == edit.Persona.Id);
            if (edit.Deleted) { if (i >= 0) values.RemoveAt(i); }
            else if (edit.AddOnly && (i >= 0 || values.Any(p => Same(p, edit.Persona)))) { }
            else if (i >= 0) { if (edit.NameChanged) values[i].Name = edit.Persona.Name; if (edit.ColorChanged) values[i].Color = edit.Persona.Color.ToLowerInvariant(); }
            else if (edit.Create || edit.AddOnly) values.Add(Copy(edit.Persona));
        }
        return values;
    }
    private static void Initialize(ChatVault vault, bool remoteExists)
    {
        var sync = vault.NameSync;
        if (sync.Migrated) return;
        var pristine = vault.Personas.Count == 1 && vault.Personas[0].Name == (vault.Role == "master" ? "Master" : "Pet")
            && vault.Personas[0].Color.Equals("#ffffff", StringComparison.OrdinalIgnoreCase);
        if (!remoteExists || !pristine)
            sync.Edits.AddRange(vault.Personas.Select(p => new ChatNameEdit { Persona = Copy(p), AddOnly = true, Create = true }));
        sync.Migrated = true;
    }
    internal static void RecordChanges(ChatVault vault)
    {
        Initialize(vault, false);
        var previous = Project(vault.NameSync);
        foreach (var persona in vault.Personas)
        {
            var old = previous.FirstOrDefault(p => p.Id == persona.Id);
            if (old is not null && Same(old, persona)) continue;
            var pending = vault.NameSync.Edits.LastOrDefault(e => e.Persona.Id == persona.Id);
            vault.NameSync.Edits.RemoveAll(e => e.Persona.Id == persona.Id);
            vault.NameSync.Edits.Add(new() { Persona = Copy(persona), Create = old is null || pending?.Create == true,
                NameChanged = old is null || old.Name != persona.Name || pending?.NameChanged == true,
                ColorChanged = old is null || !old.Color.Equals(persona.Color, StringComparison.OrdinalIgnoreCase) || pending?.ColorChanged == true });
        }
        foreach (var old in previous.Where(p => !vault.Personas.Any(v => v.Id == p.Id)))
        {
            vault.NameSync.Edits.RemoveAll(e => e.Persona.Id == old.Id);
            vault.NameSync.Edits.Add(new() { Persona = Copy(old), Deleted = true });
        }
    }
    private static ChatDevice Trusted(ChatVault vault, ChatState state, string id)
    {
        var device = state.Devices.FirstOrDefault(d => d.Id == id && d.Role == vault.Role && ChatKeySharing.Verified(vault, d));
        return device ?? throw new InvalidOperationException("Speaking names require this participant's enrolled device keys.");
    }
    internal static bool Merge(ChatVault vault, ChatState state)
    {
        var sync = vault.NameSync; var library = state.NameLibrary;
        Initialize(vault, library is not null);
        if (library is not null)
        {
            if (library.Revision < sync.Revision || sync.Revision > 0 && library.Id != sync.Id)
                throw new InvalidOperationException("Speaking-name rollback detected.");
            if (sync.Id != library.Id) { sync.Id = library.Id; sync.Key = ""; }
            if (sync.Key.Length == 0)
            {
                var envelope = library.Envelopes.FirstOrDefault(e => e.RecipientId == vault.Identity.Id);
                if (envelope is null) return false;
                sync.Key = PrivateChatCrypto.Unwrap(library.Id, 1, envelope, vault.Identity, Trusted(vault, state, envelope.DeviceId));
            }
            var content = PrivateChatCrypto.Open<ChatNameContent>(sync.Key, ["personas", library.Id, library.Revision], library.Packet, Trusted(vault, state, library.Packet.DeviceId));
            Validate(content.Personas); sync.Base = content.Personas.Select(Copy).ToList(); sync.Revision = library.Revision;
        }
        else if (sync.Revision > 0) throw new InvalidOperationException("Speaking-name rollback detected.");
        var projected = Project(sync);
        if (projected.Count == 0) { var fallback = new ChatPersona { Name = vault.Role == "master" ? "Master" : "Pet" }; projected.Add(fallback); sync.Edits.Add(new() { Persona = Copy(fallback), Create = true, NameChanged = true, ColorChanged = true }); }
        if (projected.Count > 40) throw new InvalidOperationException("Both devices' saved names exceed 40 entries. Remove unused names before synchronizing.");
        var changed = JsonSerializer.Serialize(vault.Personas, ServiceClient.Json) != JsonSerializer.Serialize(projected, ServiceClient.Json);
        vault.Personas = projected;
        if (!vault.Personas.Any(p => p.Id == vault.Active)) vault.Active = vault.Personas[0].Id;
        return changed;
    }
    internal static List<ChatQueued> Plan(ChatVault vault, ChatState state)
    {
        var sync = vault.NameSync; var result = new List<ChatQueued>(); var library = state.NameLibrary;
        if (!sync.Migrated || vault.Outbox.Any(q => q.Op == "names")) return result;
        if (library is not null && (sync.Revision != library.Revision || sync.Id != library.Id || sync.Key.Length == 0)) return result;
        var devices = state.Devices.Where(d => d.Role == vault.Role && ChatKeySharing.Verified(vault, d)).ToList();
        if (!devices.Any(d => d.Id == vault.Identity.Id)) return result;
        if (sync.Edits.Count > 0)
        {
            var values = Project(sync); Validate(values);
            if (library is not null && values.Count == sync.Base.Count && values.Zip(sync.Base).All(pair => pair.First.Id == pair.Second.Id && Same(pair.First, pair.Second)))
            { sync.Edits.Clear(); return result; }
            if (sync.Id.Length == 0) { sync.Id = Guid.NewGuid().ToString(); sync.Key = PrivateChatCrypto.NewKey(); }
            var packet = PrivateChatCrypto.Seal(sync.Key, ["personas", sync.Id, sync.Revision + 1], new ChatNameContent { Personas = values }, vault.Identity);
            result.Add(new() { Op = "names", LocalEdits = sync.Edits.Select(e => e.ChangeId).ToList(), NameChanges = JsonSerializer.Deserialize<List<ChatNameEdit>>(JsonSerializer.Serialize(sync.Edits, ServiceClient.Json), ServiceClient.Json)!, Data = JsonSerializer.SerializeToElement(new
            {
                id = sync.Id, revision = sync.Revision, deviceId = vault.Identity.Id, mutationId = Guid.NewGuid().ToString(), packet,
                envelopes = devices.Select(d => PrivateChatCrypto.Wrap(sync.Key, sync.Id, 1, d, vault.Identity)).ToList(),
            }, ServiceClient.Json) });
        }
        else if (library is not null && !vault.Outbox.Any(q => q.Op == "nameShare"))
        {
            var missing = devices.Where(d => !library.Envelopes.Any(e => e.RecipientId == d.Id)).ToList();
            if (missing.Count > 0) result.Add(new() { Op = "nameShare", Data = JsonSerializer.SerializeToElement(new
            {
                id = library.Id, deviceId = vault.Identity.Id,
                envelopes = missing.Select(d => PrivateChatCrypto.Wrap(sync.Key, library.Id, 1, d, vault.Identity)).ToList(),
            }, ServiceClient.Json) });
        }
        return result;
    }
    internal static void Accepted(ChatVault vault, ChatState state, ChatQueued item)
    {
        var data = item.Data;
        if (item.Op == "names")
        {
            var sync = vault.NameSync; var revision = data.GetProperty("revision").GetInt32() + 1;
            sync.Edits.RemoveAll(e => item.LocalEdits.Contains(e.ChangeId));
            foreach (var pending in sync.Edits)
            foreach (var sent in item.NameChanges.Where(e => e.Persona.Id == pending.Persona.Id))
            {
                if (sent.NameChanged && sent.Persona.Name == pending.Persona.Name) pending.NameChanged = false;
                if (sent.ColorChanged && sent.Persona.Color.Equals(pending.Persona.Color, StringComparison.OrdinalIgnoreCase)) pending.ColorChanged = false;
                if (sent.Create) pending.Create = false;
            }
            if (revision >= sync.Revision)
            {
                state.NameLibrary = new() { Id = data.GetProperty("id").GetString()!, Revision = revision,
                    Packet = data.GetProperty("packet").Deserialize<ChatPacket>(ServiceClient.Json)!,
                    Envelopes = data.GetProperty("envelopes").Deserialize<List<ChatEnvelope>>(ServiceClient.Json)! };
            }
            Merge(vault, state);
        }
        else if (item.Op == "nameShare" && state.NameLibrary is { } library)
        {
            var additions = data.GetProperty("envelopes").Deserialize<List<ChatEnvelope>>(ServiceClient.Json)!;
            library.Envelopes.RemoveAll(e => additions.Any(a => a.RecipientId == e.RecipientId)); library.Envelopes.AddRange(additions);
        }
    }
    internal static void Conflict(ChatVault vault) => vault.Outbox.RemoveAll(q => q.Op is "names" or "nameShare");
}
