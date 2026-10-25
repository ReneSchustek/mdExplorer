using System.Globalization;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace MdExplorer.App.Logging;

/// <summary>
/// Serilog-Sink mit Ringpuffer für den integrierten Log-Viewer, zugleich dessen Lesezugang
/// <see cref="IMemoryLogStore"/>. Ist der Puffer voll, fällt der älteste Eintrag heraus; der
/// Speicher wächst also nicht mit der Laufzeit.
/// </summary>
internal sealed class MemorySink : ILogEventSink, IMemoryLogStore
{
    /// <summary>
    /// Obergrenze des Puffers in Einträgen. Der Speicherbedarf hängt an der Länge der Nachrichten;
    /// ein Eintrag mit Aufrufstapel belegt mehrere Kilobyte.
    /// </summary>
    public const int DefaultCapacity = 2000;

    private static readonly Lazy<MemorySink> LazyInstance =
        new(static () => new MemorySink(DefaultCapacity));

    private readonly Queue<LogEntry> _buffer = new();
    private readonly int _capacity;
    private readonly object _lock = new();

    /// <summary>
    /// Sichtbar für Tests; Produktivcode geht über <see cref="Instance"/>,
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
        // Außerhalb der Sperre: Der Log-Viewer springt synchron auf den UI-Thread. Ruft der
        // gerade Snapshot() und wartet auf diese Sperre, warteten beide Threads aufeinander.
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

    /// <inheritdoc />
    /// <remarks>
    /// Die Betriebsanzeige liest bei jedem neuen Eintrag nach. Sie braucht nur das Ende des
    /// Puffers; eine Kopie aller Einträge je Protokollzeile wäre die zehnfache Arbeit.
    /// </remarks>
    public IReadOnlyList<LogEntry> Recent(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        lock (_lock)
        {
            return [.. _buffer.Skip(Math.Max(0, _buffer.Count - count))];
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
