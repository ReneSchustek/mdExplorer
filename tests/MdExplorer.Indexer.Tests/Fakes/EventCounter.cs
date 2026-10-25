namespace MdExplorer.Indexer.Tests.Fakes;

/// <summary>
/// Zählt ein wiederkehrendes Ereignis und lässt auf ein Ereignis nach einer Marke warten.
/// </summary>
/// <remarks>
/// Ein Semaphor merkt sich auch Ereignisse, die vor dem Warten lagen; der Test kehrte dann zu
/// früh zurück, etwa beim Speichern des Erst-Scans statt beim Speichern des gerade ausgelösten
/// Ereignisses. Mit einer Marke, genommen vor dem Auslösen, zählt nur, was danach geschieht.
/// </remarks>
internal sealed class EventCounter
{
    private readonly object _gate = new();
    private readonly List<(long Target, TaskCompletionSource Done)> _waiters = [];
    private long _count;

    public long Mark
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    public void Signal()
    {
        List<TaskCompletionSource> reached = [];
        lock (_gate)
        {
            _count++;
            foreach ((long target, TaskCompletionSource done) in _waiters.Where(waiter => waiter.Target <= _count).ToList())
            {
                reached.Add(done);
                _ = _waiters.Remove((target, done));
            }
        }
        foreach (TaskCompletionSource done in reached)
        {
            _ = done.TrySetResult();
        }
    }

    /// <summary>Wartet, bis seit <paramref name="mark"/> mindestens <paramref name="count"/> Ereignisse kamen.</summary>
    public async Task<bool> WaitAsync(long mark, int count, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task waiting;
        lock (_gate)
        {
            if (_count >= mark + count)
            {
                return true;
            }
            TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((mark + count, done));
            waiting = done.Task;
        }

        try
        {
            await waiting.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
