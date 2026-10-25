using Microsoft.Extensions.Logging;

namespace MdExplorer.Graph.Tests.Fakes;

/// <summary>Merkt sich Stufe und Ereignis-Id jedes Eintrags.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, int EventId)> _entries = [];

    public IReadOnlyList<(LogLevel Level, int EventId)> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) => _entries.Add((logLevel, eventId.Id));
}
