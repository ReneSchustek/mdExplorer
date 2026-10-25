using Microsoft.Extensions.Logging;

namespace MdExplorer.Core.Tests.Fakes;

/// <summary>
/// Hält Protokolleinträge fest. <see cref="FirstError"/> erfüllt sich beim ersten Eintrag ab
/// Stufe Error; ein Test wartet darauf statt auf eine feste Zeit.
/// </summary>
/// <typeparam name="T">Kategorie des Loggers.</typeparam>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly TaskCompletionSource<Exception?> _firstError = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();
    private readonly List<(LogLevel Level, EventId EventId)> _entries = [];

    public Task<Exception?> FirstError => _firstError.Task;

    public IReadOnlyList<(LogLevel Level, EventId EventId)> Entries
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

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_gate)
        {
            _entries.Add((logLevel, eventId));
        }
        if (logLevel >= LogLevel.Error)
        {
            _ = _firstError.TrySetResult(exception);
        }
    }
}
