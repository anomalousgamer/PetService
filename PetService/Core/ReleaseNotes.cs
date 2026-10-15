namespace PetService.Core;

internal sealed record ChangelogSection(string Title, string[] Riddles);

internal static class ReleaseNotes
{
    internal const string Version = "0.6.0.0";
    internal static readonly ChangelogSection[] Sections =
    [
        new("A few new ribbons",
        [
            "Several bells may share one ribbon, and some ribbons wait for a single dawn.",
            "A letter may choose its hour, its doorway, and the answers waiting at its seal.",
            "Two little jars remember every pebble, and treasures wait for a nod.",
            "A borrowed voice may soften, tumble, or learn the syllables of another song.",
            "Both hands may read tomorrow's ribbons; only one may move them.",
            "Many little rooms remember their voices and colors; their letters stay sealed between two doors.",
            "An empty music box waits for the hand that chooses its songs and the moments they may play.",
        ]),
    ];
}
