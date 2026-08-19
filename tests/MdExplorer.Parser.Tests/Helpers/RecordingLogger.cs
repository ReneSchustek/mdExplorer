using Microsoft.Extensions.Logging;

namespace MdExplorer.Parser.Tests.Helpers;

/// <summary>
/// Zeichnet auf, was protokolliert wurde. Nötig, um zu prüfen, ob ein Eintrag den vollen
/// Aufrufstapel mitführt oder nicht — genau das unterscheidet den ersten Fehlschlag einer
/// Datei von jedem weiteren.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<RecordedLogEntry> _entries = [];
    private readonly object _gate = new();

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
        }
    }
}

internal sealed record RecordedLogEntry(LogLevel Level, int EventId, string Message, Exception? Exception);
