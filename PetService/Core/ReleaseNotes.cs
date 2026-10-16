namespace PetService.Core;

internal sealed record ChangelogSection(string Title, string[] Riddles);

internal static class ReleaseNotes
{
    internal const string Version = "0.6.0.4";
    internal static readonly ChangelogSection[] Sections =
    [
        new("A clearer conversation", [
            "Master is really trying to fix things.",
        ]),
    ];
}
