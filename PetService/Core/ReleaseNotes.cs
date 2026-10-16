namespace PetService.Core;
internal sealed record ChangelogSection(string Title,string[] Riddles);
internal static class ReleaseNotes
{
    internal const string Version="0.6.0.5";
    internal static readonly ChangelogSection[] Sections=[
        new("Ima is Dumb Saga",[
            "What even am I doing now?????",
        ]),
    ];
}
