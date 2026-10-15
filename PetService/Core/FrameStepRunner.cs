namespace PetService.Core;

// A failed optional component must not starve the connection or other updates.
// Retry failures slowly, and log each ongoing failure at most every 30 seconds.
internal sealed class FrameStepRunner
{
    private sealed class Failure
    {
        internal long RetryAt;
        internal long LogAt;
    }

    private readonly Dictionary<string, Failure> failures = new();
    internal IReadOnlyCollection<string> FailedSteps => failures.Keys;

    internal bool Run(string name, System.Action update, long tick,
        System.Action<string, Exception>? report = null)
    {
        if (failures.TryGetValue(name, out var failure) && tick < failure.RetryAt)
            return false;

        try
        {
            update();
            failures.Remove(name);
            return true;
        }
        catch (Exception error)
        {
            if (failure is null)
                failures[name] = failure = new();
            failure.RetryAt = tick + 1000;
            if (tick >= failure.LogAt)
            {
                failure.LogAt = tick + 30000;
                // Logging itself must never become another update blocker.
                try { report?.Invoke(name, error); } catch { }
            }
            return false;
        }
    }

    internal void RetryNow() => failures.Clear();
}
