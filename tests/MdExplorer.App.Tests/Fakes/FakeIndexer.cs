using MdExplorer.Indexer.Abstractions;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>
/// Indexer, dessen Fortschritt der Test selbst meldet und dessen Durchläufe er zählt, einmal
/// scheitern oder bis zum Abbruch hängen lässt.
/// </summary>
internal sealed class FakeIndexer : IIndexer
{
    private readonly List<CancellationToken> _scanTokens = [];
    private readonly Lock _lock = new();
    private int _callCount;

    public event EventHandler<IndexerScanProgressEventArgs>? InitialScanProgress;

    /// <summary>Anzahl der begonnenen Durchläufe; threadsicher, weil die Brücke sie im Hintergrund startet.</summary>
    public int CallCount => Volatile.Read(ref _callCount);

    /// <summary>Der Fehler, mit dem der nächste Durchlauf scheitert; danach gelingen sie wieder.</summary>
    public Exception? ThrowOnNextCall { get; set; }

    /// <summary>
    /// Wenn gesetzt, endet jeder Durchlauf erst mit seinem Abbruch. So bleibt ein Durchlauf
    /// stehen, und der Test sieht, ob ein späterer ihn abbricht.
    /// </summary>
    public bool HoldScans { get; set; }

    /// <summary>
    /// Wenn gesetzt, endet ein gehaltener Durchlauf nach seinem Abbruch erst, wenn der Test das Tor
    /// öffnet. So sieht der Test, ob das Beenden auf den laufenden Durchlauf wartet.
    /// </summary>
    public TaskCompletionSource? AfterCancellation { get; set; }

    /// <summary>Erfüllt, sobald ein gehaltener Durchlauf seinen Abbruch bemerkt hat.</summary>
    public TaskCompletionSource CancellationSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Die Abbruchmerker der begonnenen Durchläufe in ihrer Reihenfolge.</summary>
    public IReadOnlyList<CancellationToken> ScanTokens
    {
        get
        {
            lock (_lock)
            {
                return [.. _scanTokens];
            }
        }
    }

    public Task RunInitialScanAsync(CancellationToken cancellationToken)
    {
        _ = Interlocked.Increment(ref _callCount);
        lock (_lock)
        {
            _scanTokens.Add(cancellationToken);
        }

        Exception? toThrow = ThrowOnNextCall;
        if (toThrow is not null)
        {
            ThrowOnNextCall = null;
            return Task.FromException(toThrow);
        }

        return HoldScans ? HoldAsync(cancellationToken) : Task.CompletedTask;
    }

    public void Raise(IndexerScanProgressEventArgs args) =>
        InitialScanProgress?.Invoke(this, args);

    public void RaiseProgress(string root, int processedCount, bool isCompleted) =>
        Raise(new IndexerScanProgressEventArgs(root, processedCount, isCompleted));

    private async Task HoldAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = CancellationSeen.TrySetResult();
            if (AfterCancellation is { } gate)
            {
                await gate.Task.ConfigureAwait(false);
            }
        }
    }
}
