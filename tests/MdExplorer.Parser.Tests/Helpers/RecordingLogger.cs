using Microsoft.Extensions.Logging;

namespace MdExplorer.Parser.Tests.Helpers;

/// <summary>
/// Zeichnet auf, was protokolliert wurde. Nötig, um zu prüfen, ob ein Eintrag den vollen
/// Aufrufstapel mitführt oder nicht. Das unterscheidet den ersten Fehlschlag einer
/// Datei von jedem weiteren.
/// </summary>
/// <remarks>
/// Tests der Hintergrundschleife warten über <see cref="WaitForCountAsync"/> auf Einträge statt
/// auf eine Uhr: Der Orchestrator schreibt am Ende jedes Durchlaufs genau einen, und darauf ist
/// Verlass, egal wie ausgelastet die Maschine ist.
/// </remarks>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<RecordedLogEntry> _entries = [];
    private readonly List<(Func<RecordedLogEntry, bool> Match, int Count, TaskCompletionSource Done)> _waiters = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<RecordedLogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        RecordedLogEntry entry = new(logLevel, eventId.Id, formatter(state, exception), exception);
        lock (_gate)
        {
            _entries.Add(entry);
            ReleaseSatisfiedWaiters();
        }
    }

    /// <summary>Wartet, bis mindestens <paramref name="count"/> passende Einträge vorliegen.</summary>
    /// <param name="match">Welche Einträge zählen.</param>
    /// <param name="count">Wie viele es sein müssen.</param>
    /// <param name="timeout">Schutz gegen einen hängenden Test, keine Synchronisation.</param>
    public Task WaitForCountAsync(Func<RecordedLogEntry, bool> match, int count, TimeSpan timeout)
    {
        TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _waiters.Add((match, count, done));
            ReleaseSatisfiedWaiters();
        }
        return done.Task.WaitAsync(timeout);
    }

    private void ReleaseSatisfiedWaiters()
    {
        foreach ((Func<RecordedLogEntry, bool> match, int count, TaskCompletionSource done) in _waiters.ToArray())
        {
            if (_entries.Count(match) >= count)
            {
                _ = _waiters.Remove((match, count, done));
                done.SetResult();
            }
        }
    }
}

/// <summary>Ein aufgezeichneter Protokolleintrag mit Ebene, Ereignis-Id, Text und Ausnahme.</summary>
internal sealed record RecordedLogEntry(LogLevel Level, int EventId, string Message, Exception? Exception);
