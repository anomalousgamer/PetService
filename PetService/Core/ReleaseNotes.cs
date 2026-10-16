namespace PetService.Core;
internal sealed record ChangelogSection(string Title,string[] Riddles);
internal static class ReleaseNotes
{
    internal const string Version="0.6.0.6";
    internal static readonly ChangelogSection[] Sections=[
        new("While the lantern burns",[
            "One key may open the door until the lantern goes out.",
            "A name and its chosen shade may travel between mirrors.",
            "Each voice keeps its own unfinished page.",
            "The quill waits beneath your hand after each whisper.",
            "Unopened letters leave a light at the door.",
            "A conversation may keep a window of its own.",
            "A familiar call now reaches the far side of the door.",
            "The mirror listens while your eyes follow other footsteps.",
            "A borrowed voice remembers which doors it may pass unchanged.",
            "At the threshold, choose which whisper wakes beside you.",
        ]),
    ];
}
