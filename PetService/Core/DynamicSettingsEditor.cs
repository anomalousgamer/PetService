using System.Text.Json;

namespace PetService.Core;

internal sealed class DynamicSettingsEditor
{
    internal string Pet {get;private set;}="";
    internal DynamicSettings Value=new();
    internal string Contacts="";
    private string savedValue="",savedContacts="";
    internal bool Dirty=>Pet.Length>0 && (Stamp(Value)!=savedValue || Contacts!=savedContacts);
    internal bool SavedChanged {get;private set;}
    private static string Stamp(DynamicSettings value)=>JsonSerializer.Serialize(value,ServiceClient.Json);
    internal static DynamicSettings Clone(DynamicSettings value)
    {
        var copy=JsonSerializer.Deserialize<DynamicSettings>(Stamp(value),ServiceClient.Json)!;
        copy.NormalizeContacts();return copy;
    }
    internal void Receive(string pet,DynamicSettings settings,bool discardEdits=false)
    {
        var clean=Clone(settings);var stamp=Stamp(clean);
        if(Pet==pet && Dirty && !discardEdits){SavedChanged=stamp!=savedValue;return;}
        if(Pet==pet && stamp==savedValue && !discardEdits)return;
        Pet=pet;Value=clean;Contacts=string.Join('\n',clean.Contacts);savedValue=stamp;savedContacts=Contacts;SavedChanged=false;
    }
    internal void Clear(){Pet="";Value=new();Contacts="";savedValue="";savedContacts="";SavedChanged=false;}
}
