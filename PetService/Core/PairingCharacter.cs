namespace PetService.Core;

// Capture the intended character before the network request starts. Switching
// characters while it completes must not authorize the new character.
internal sealed record PairingCharacter(string Id,string Name,string HomeWorld)
{
    internal void Approve(List<CharacterAccess> characters,bool newProfile)
    {
        var entry=characters.FirstOrDefault(x=>x.Id==Id);
        if(entry is null)characters.Add(new(){Id=Id,Name=Name,HomeWorld=HomeWorld,Allowed=true});
        else if(newProfile)entry.Allowed=true;
    }
}
