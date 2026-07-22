using MdExplorer.App.Logging;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace MdExplorer.App.Tests.Logging;

/// <summary>Tests für den Ring-Buffer-Sink.</summary>
public sealed class MemorySinkTests
{
    [Fact]
    public void Emit_OnSingleEvent_AppendsToSnapshot()
    {
        MemorySink sut = new(capacity: 8);
        sut.Emit(BuildEvent("Hello {Name}", LogEventLevel.Information, "Welt", "Demo.Source"));

        IReadOnlyList<LogEntry> snapshot = sut.Snapshot();
        LogEntry entry = Assert.Single(snapshot);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Demo.Source", entry.SourceContext);
        Assert.Contains("Welt", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_OnExceedingCapacity_DropsOldestEntries()
    {
        const int capacity = 3;
        MemorySink sut = new(capacity);
        for (int i = 0; i < capacity + 2; i++)
        {
            sut.Emit(BuildEvent("Msg " + i, LogEventLevel.Debug, parameter: null, sourceContext: "S"));
        }

        IReadOnlyList<LogEntry> snapshot = sut.Snapshot();
        Assert.Equal(capacity, snapshot.Count);
        Assert.Contains("Msg 2", snapshot[0].Message, StringComparison.Ordinal);
        Assert.Contains("Msg 4", snapshot[^1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_FiresEntryAdded()
    {
        MemorySink sut = new(capacity: 4);
        List<LogEntry> received = [];
        sut.EntryAdded += (_, entry) => received.Add(entry);

        sut.Emit(BuildEvent("Hi", LogEventLevel.Warning, parameter: null, sourceContext: "W"));

        LogEntry entry = Assert.Single(received);
        Assert.Equal(LogLevel.Warning, entry.Level);
    }

    [Fact]
    public void Ctor_OnNonPositiveCapacity_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new MemorySink(0));

    private static LogEvent BuildEvent(
        string template,
        LogEventLevel level,
        string? parameter,
        string sourceContext)
    {
        MessageTemplateParser parser = new();
        MessageTemplate parsed = parser.Parse(template);
        List<LogEventProperty> properties = [];
        if (parameter is not null)
        {
            properties.Add(new LogEventProperty("Name", new ScalarValue(parameter)));
        }
        properties.Add(new LogEventProperty("SourceContext", new ScalarValue(sourceContext)));
        return new LogEvent(
            DateTimeOffset.UtcNow,
            level,
            exception: null,
            parsed,
            properties);
    }
}
