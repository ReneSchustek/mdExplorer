using MdExplorer.App.Logging;
using MdExplorer.App.Services;
using MdExplorer.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Tests.Services;

/// <summary>Tests für den Health-Aggregator des MainWindow-LED.</summary>
public sealed class OperationHealthProviderTests
{
    [Fact]
    public void EmptyStore_ReportsHealthy()
    {
        FakeStore store = new();
        ParseFailureStatus failureStatus = new();
        using OperationHealthProvider sut = new(store, failureStatus);

        Assert.Equal(OperationHealth.Healthy, sut.Current);
        Assert.Contains("normal", sut.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnError_ReportsErrorAndFiresChanged()
    {
        FakeStore store = new();
        ParseFailureStatus failureStatus = new();
        using OperationHealthProvider sut = new(store, failureStatus);
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
        ParseFailureStatus failureStatus = new();
        using OperationHealthProvider sut = new(store, failureStatus);

        store.Add(LogLevel.Warning, "Spike");

        Assert.Equal(OperationHealth.Warning, sut.Current);
        Assert.Contains("Spike", sut.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorBeatsWarning_InSameWindow()
    {
        FakeStore store = new();
        ParseFailureStatus failureStatus = new();
        using OperationHealthProvider sut = new(store, failureStatus);

        store.Add(LogLevel.Warning, "weiches Problem");
        store.Add(LogLevel.Error, "harter Fehler");

        Assert.Equal(OperationHealth.Error, sut.Current);
    }

    [Fact]
    public void OnUnparsableFiles_ReportsWarningWithCount()
    {
        FakeStore store = new();
        ParseFailureStatus failureStatus = new();
        using OperationHealthProvider sut = new(store, failureStatus);

        failureStatus.Update(3);

        Assert.Equal(OperationHealth.Warning, sut.Current);
        Assert.Contains("3 Dateien nicht verarbeitbar.", sut.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void OnSingleUnparsableFile_UsesSingularWording()
    {
        FakeStore store = new();
        ParseFailureStatus failureStatus = new();
        using OperationHealthProvider sut = new(store, failureStatus);

        failureStatus.Update(1);

        Assert.Contains("1 Datei nicht verarbeitbar.", sut.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("normal", sut.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnUnparsableFilesWithError_KeepsErrorAndNamesBoth()
    {
        FakeStore store = new();
        ParseFailureStatus failureStatus = new();
        using OperationHealthProvider sut = new(store, failureStatus);

        store.Add(LogLevel.Error, "Boom");
        failureStatus.Update(2);

        Assert.Equal(OperationHealth.Error, sut.Current);
        Assert.Contains("Boom", sut.Detail, StringComparison.Ordinal);
        Assert.Contains("2 Dateien nicht verarbeitbar.", sut.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void OnUnparsableFilesResolved_ReturnsToHealthy()
    {
        FakeStore store = new();
        ParseFailureStatus failureStatus = new();
        using OperationHealthProvider sut = new(store, failureStatus);
        failureStatus.Update(1);

        failureStatus.Update(0);

        Assert.Equal(OperationHealth.Healthy, sut.Current);
        Assert.Contains("normal", sut.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AfterDispose_StatusChangeIsIgnored()
    {
        FakeStore store = new();
        ParseFailureStatus failureStatus = new();
        OperationHealthProvider sut = new(store, failureStatus);
        sut.Dispose();

        failureStatus.Update(5);

        Assert.Equal(OperationHealth.Healthy, sut.Current);
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
