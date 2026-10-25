namespace MdExplorer.Core.Tests.Fakes;

/// <summary>
/// Sammelt alle <see cref="SynchronizationContext.Post"/>-Aufrufe und führt sie erst bei
/// <see cref="DrainPending"/> aus. So lässt sich prüfen, ob ein Ereignis über den Kontext des
/// Aufrufers zugestellt wird, und ob überhaupt eines kommt, ohne auf eine Wanduhr zu warten.
/// </summary>
internal sealed class QueueingSynchronizationContext : SynchronizationContext
{
    private readonly List<(SendOrPostCallback Callback, object? State)> _pending = [];
    private readonly Lock _gate = new();

    public int PostCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        lock (_gate)
        {
            _pending.Add((d, state));
        }
    }

    public void DrainPending()
    {
        (SendOrPostCallback Callback, object? State)[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _pending];
            _pending.Clear();
        }
        foreach ((SendOrPostCallback callback, object? state) in snapshot)
        {
            callback(state);
        }
    }

    /// <summary>Führt <paramref name="action"/> auf einem Worker aus, auf dem dieser Kontext gilt.</summary>
    public Task RunWithinAsync(Func<Task> action) => Task.Run(async () =>
    {
        SetSynchronizationContext(this);
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            SetSynchronizationContext(null);
        }
    });
}
