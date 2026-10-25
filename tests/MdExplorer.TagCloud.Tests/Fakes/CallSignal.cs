namespace MdExplorer.TagCloud.Tests.Fakes;

/// <summary>
/// Zählt Aufrufe einer Attrappe und lässt einen Test auf den n-ten warten, ohne Uhr und ohne
/// Pause: Die Wartezeit endet mit dem Aufruf selbst, die Frist greift nur, wenn etwas hängt.
/// </summary>
internal sealed class CallSignal
{
    private readonly Lock _gate = new();
    private readonly List<(int Target, TaskCompletionSource Done)> _waiters = [];
    private int _count;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    public void Record()
    {
        List<TaskCompletionSource> reached;
        lock (_gate)
        {
            _count++;
            reached = [.. _waiters.Where(waiter => waiter.Target <= _count).Select(waiter => waiter.Done)];
            _ = _waiters.RemoveAll(waiter => waiter.Target <= _count);
        }
        foreach (TaskCompletionSource done in reached)
        {
            _ = done.TrySetResult();
        }
    }

    /// <summary>Endet, sobald <paramref name="target"/> Aufrufe gezählt sind; <see langword="false"/> nach der Frist.</summary>
    public async Task<bool> WaitForAsync(int target, TimeSpan timeout)
    {
        TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_count >= target)
            {
                return true;
            }
            _waiters.Add((target, done));
        }
        try
        {
            await done.Task.WaitAsync(timeout, TestContext.Current.CancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
