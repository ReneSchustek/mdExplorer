namespace MdExplorer.Update.Services;

/// <summary>
/// Bricht einen Lesevorgang ab, der länger als die Stillstandsgrenze keine Daten liefert.
/// </summary>
/// <remarks>
/// Die Frist beginnt bei jedem Lesevorgang neu. Eine Gesamtfrist träfe einen langsamen, aber
/// laufenden Download genauso wie eine Verbindung, die steht.
/// </remarks>
internal sealed class StallGuard : IDisposable
{
    private readonly TimeSpan _stallTimeout;
    private readonly CancellationToken _callerToken;
    private readonly CancellationTokenSource _stall;
    private readonly CancellationTokenSource _linked;

    public StallGuard(TimeSpan stallTimeout, TimeProvider timeProvider, CancellationToken callerToken)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _stallTimeout = stallTimeout;
        _callerToken = callerToken;
        // Über die Uhr erzeugt, damit auch CancelAfter ihr folgt; Tests stellen die Uhr vor.
        _stall = new CancellationTokenSource(Timeout.InfiniteTimeSpan, timeProvider);
        _linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _stall.Token);
    }

    /// <summary>Liest in <paramref name="buffer"/>; wirft <see cref="TimeoutException"/> bei Stillstand.</summary>
    public async Task<int> ReadAsync(Stream source, byte[] buffer)
    {
        _stall.CancelAfter(_stallTimeout);
        try
        {
            return await source.ReadAsync(buffer, _linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (_stall.IsCancellationRequested && !_callerToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Seit {_stallTimeout} kamen keine Daten mehr.", ex);
        }
    }

    public void Dispose()
    {
        _linked.Dispose();
        _stall.Dispose();
    }
}
