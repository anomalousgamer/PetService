namespace PetService.Core;

// All local prompts and synchronization share this switch. A response from an
// earlier connection must stay invalid even if the user has already resumed.
internal sealed class MasterInputControl : IDisposable
{
    private CancellationTokenSource requests = new();

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
        if (Enabled == enabled) return;
        Enabled = enabled;
        Revision++;
        requests.Cancel();
        requests.Dispose();
        requests = new();
        if (!enabled) requests.Cancel();
    }

    public void Invalidate(){Revision++;requests.Cancel();requests.Dispose();requests=new();if(!Enabled)requests.Cancel();}
    public bool Accepts(long revision) => Enabled && revision == Revision;

    public void Dispose()
    {
        requests.Cancel();
        requests.Dispose();
    }
}
