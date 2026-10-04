namespace PetService.Core;

internal static class ReplyChoicePolicy
{
    internal static bool Allows(Prompt prompt,int? index) => prompt.Kind=="message"
        && index is >=0 and <32 && prompt.Choices is not null && index.Value<prompt.Choices.Count
        && !string.IsNullOrWhiteSpace(prompt.Choices[index.Value]);
}
