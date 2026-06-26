using System.Globalization;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace MdExplorer.App.Logging;

/// <summary>
/// Serilog-Sink mit Ring-Buffer (Default-Kapazität <see cref="DefaultCapacity"/>).
/// Speist den integrierten Log-Viewer und implementiert gleichzeitig
/// <see cref="IMemoryLogStore"/> als Lese-Abstraktion.
/// Thread-safe: älteste Einträge werden bei Kapazitätsüberschreitung verworfen
/// (logging.md Z.158-181 — harte Grenze, kein unbeschränktes Wachstum).
/// </summary>
internal sealed class MemorySink : ILogEventSink, IMemoryLogStore
{
    /// <summary>Default-Kapazität gemäß <c>logging.md</c> (≈ 300 kB Heap).</summary>
    public const int DefaultCapacity = 2000;

    private static readonly Lazy<MemorySink> LazyInstance =
        new(static () => new MemorySink(DefaultCapacity));

    private readonly Queue<LogEntry> _buffer = new();
    private readonly int _capacity;
    private readonly object _lock = new();

    /// <summary>
    /// Sichtbar für Tests — Produktivcode geht über <see cref="Instance"/>,
    /// damit der Serilog-Bootstrap und der DI-Container dieselbe Ringpuffer-Instanz teilen.
    /// </summary>
    internal MemorySink(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Kapazität muss > 0 sein.");
        }
        _capacity = capacity;
    }

    /// <summary>Prozessweite Singleton-Instanz. Wird sowohl von Serilog als auch vom DI-Container geteilt.</summary>
    public static MemorySink Instance => LazyInstance.Value;

    /// <inheritdoc />
    public int Capacity => _capacity;

    /// <inheritdoc />
    public event EventHandler<LogEntry>? EntryAdded;

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        LogEntry entry = ToEntry(logEvent);
        lock (_lock)
        {
            _buffer.Enqueue(entry);
            while (_buffer.Count > _capacity)
            {
                _ = _buffer.Dequeue();
            }
        }
        EntryAdded?.Invoke(this, entry);
    }

    /// <inheritdoc />
    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_lock)
        {
            return [.. _buffer];
        }
    }

    private static LogEntry ToEntry(LogEvent logEvent)
    {
        string sourceContext = string.Empty;
        if (logEvent.Properties.TryGetValue("SourceContext", out LogEventPropertyValue? sourceValue)
            && sourceValue is ScalarValue scalar
            && scalar.Value is string contextString)
        {
            sourceContext = contextString;
        }
        string message = logEvent.RenderMessage(CultureInfo.InvariantCulture);
        string? exception = logEvent.Exception?.ToString();
        LogLevel level = MapLevel(logEvent.Level);
        return new LogEntry(logEvent.Timestamp, level, sourceContext, message, exception);
    }

    private static LogLevel MapLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => LogLevel.Trace,
        LogEventLevel.Debug => LogLevel.Debug,
        LogEventLevel.Information => LogLevel.Information,
        LogEventLevel.Warning => LogLevel.Warning,
        LogEventLevel.Error => LogLevel.Error,
        LogEventLevel.Fatal => LogLevel.Critical,
        _ => LogLevel.Information,
    };
}
