namespace PetService.Core;

public enum ChatLoginRole { Off, Pet, Master }

internal sealed class ChatLoginStartup
{
    private string character="";
    private bool handled;

    internal void Reset(){character="";handled=false;}

    internal ChatLoginRole Update(bool loggedIn,string characterId,bool ready,ChatLoginRole role,bool paired,bool allowed)
    {
        if(!loggedIn){Reset();return ChatLoginRole.Off;}
        if(characterId.Length==0 || !ready)return ChatLoginRole.Off;
        if(character!=characterId){character=characterId;handled=false;}
        if(handled)return ChatLoginRole.Off;
        handled=true;
        return role==ChatLoginRole.Master?ChatLoginRole.Master
            :role==ChatLoginRole.Pet && paired && allowed?ChatLoginRole.Pet:ChatLoginRole.Off;
    }
}
