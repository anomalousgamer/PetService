namespace PetService.Core;

// Finish independent cleanup even when one component fails. Native hook failures
// remain visible and retryable; an installed detour must never be forgotten.
internal sealed class ShutdownSequence
{
    private sealed record Step(string Name, Action Release, bool Required);
    private readonly List<Step> pending = [];

    internal void Add(string name, Action release, bool required = false)
        => pending.Add(new(name, release, required));

    internal void Run(Action<string, Exception> report)
    {
        var failures = new List<Exception>();
        foreach (var step in pending.ToArray())
        {
            try
            {
                step.Release();
                pending.Remove(step);
            }
            catch (Exception error)
            {
                try { report(step.Name, error); } catch { }
                if (step.Required)
                    failures.Add(new InvalidOperationException("Could not release " + step.Name, error));
                else
                    pending.Remove(step);
            }
        }
        if (failures.Count > 0)
            throw new AggregateException("Some Pet Service hooks or handlers could not be removed. See the logged cleanup step.", failures);
    }
}
