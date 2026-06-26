using MdExplorer.App.Logging;
using MdExplorer.App.Services;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Tests.Services;

/// <summary>Tests fuer den Health-Aggregator des MainWindow-LED.</summary>
public sealed class OperationHealthProviderTests
{
    [Fact]
    public void EmptyStore_ReportsHealthy()
    {
        FakeStore store = new();
        using OperationHealthProvider sut = new(store);

        Assert.Equal(OperationHealth.Healthy, sut.Current);
        Assert.Contains("normal", sut.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnError_ReportsErrorAndFiresChanged()
    {
        FakeStore store = new();
        using OperationHealthProvider sut = new(store);
        int changedCount = 0;
        sut.Changed += (_, _) => changedCount++;

        store.Add(LogLevel.Error, "Boom");

        Assert.Equal(OperationHealth.Error, sut.Current);
        Assert.Contains("Boom", sut.Detail, StringComparison.Ordinal);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void OnWarning_OverridesHealthy()
    {
        FakeStore store = new();
        using OperationHealthProvider sut = new(store);

        store.Add(LogLevel.Warning, "Spike");

        Assert.Equal(OperationHealth.Warning, sut.Current);
        Assert.Contains("Spike", sut.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorBeatsWarning_InSameWindow()
    {
        FakeStore store = new();
        using OperationHealthProvider sut = new(store);

        store.Add(LogLevel.Warning, "weiches Problem");
        store.Add(LogLevel.Error, "harter Fehler");

        Assert.Equal(OperationHealth.Error, sut.Current);
    }

    private sealed class FakeStore : IMemoryLogStore
    {
        private readonly List<LogEntry> _entries = [];

        public int Capacity => 1000;

        public event EventHandler<LogEntry>? EntryAdded;

        public void Add(LogLevel level, string message)
        {
            LogEntry entry = new(DateTimeOffset.UtcNow, level, "Test", message, null);
            _entries.Add(entry);
            EntryAdded?.Invoke(this, entry);
        }

        public IReadOnlyList<LogEntry> Snapshot() => [.. _entries];
    }
}
