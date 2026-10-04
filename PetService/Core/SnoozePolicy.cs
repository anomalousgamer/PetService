namespace PetService.Core;

public static class SnoozePolicy
{
    public const int DefaultMinutes = 10;
    public const int MaximumMinutes = 1440;
    public static bool IsValid(int? minutes) => minutes is >= 1 and <= MaximumMinutes;
}
