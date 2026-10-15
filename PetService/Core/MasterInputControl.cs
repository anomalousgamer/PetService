namespace PetService.Core;

// All local prompts and synchronization share this switch. A response from an
// earlier connection must stay invalid even if the user has already resumed.
internal sealed class MasterInputControl : IDisposable
{
    private CancellationTokenSource requests = new();
    private bool disposed;
    internal Exception? LastCancellationError { get; private set; }

    public bool Enabled { get; private set; }
    public long Revision { get; private set; }
    public CancellationToken RequestCancellation => requests.Token;

    public MasterInputControl(bool enabled)
    {
        Enabled = enabled;
        if (!enabled) requests.Cancel();
    }

    public void SetEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Enabled == enabled) return;
        Enabled = enabled;
        Revision++;
        ReplaceRequests();
    }

    private void Cancel(CancellationTokenSource source)
    {
        try { source.Cancel(); LastCancellationError = null; }
        catch (AggregateException error) { LastCancellationError = error; }
    }

    private void ReplaceRequests()
    {
        var previous = requests;
        requests = new();
        if (!Enabled) requests.Cancel();
        try { Cancel(previous); }
        finally { previous.Dispose(); }
    }

    public void Invalidate(){ObjectDisposedException.ThrowIf(disposed,this);Revision++;ReplaceRequests();}
    public bool Accepts(long revision) => !disposed && Enabled && revision == Revision;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Enabled = false;
        Revision++;
        try { Cancel(requests); }
        finally { requests.Dispose(); }
    }
}
