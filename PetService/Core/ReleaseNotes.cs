namespace PetService.Core;

internal sealed record ChangelogSection(string Title, string[] Riddles);

internal static class ReleaseNotes
{
    internal const string Version = "0.6.0.2";
    internal static readonly ChangelogSection[] Sections =
    [
        new("", ["Master broke something but it should be fixed now."]),
    ];
}
