using MdExplorer.App.Localization;
using MdExplorer.App.Logging;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Die Betriebsanzeige im Hauptfenster zeigt den schwersten Zustand: Fehler vor Warnung vor
/// Normalbetrieb. Nicht verarbeitbare Dateien zählen als Warnung mit Zahl und verdrängen einen
/// gleichzeitigen Fehler nicht.
/// </summary>
public sealed class OperationHealthProviderTests
{
    [Fact]
    public void Current_OnAnEmptyStore_ReportsHealthy()
    {
        FakeStore store = new();
        ParseFailureStatus failureStatus = new();
        using OperationHealthProvider sut = new(store, failureStatus, new BackgroundServiceStatus());

        Assert.Equal(OperationHealth.Healthy, sut.Current);
        Assert.Contains("normal", sut.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Current_WithAStoppedService_StaysOnErrorAndNamesIt()
    {
        FakeStore store = new();
        BackgroundServiceStatus serviceStatus = new();
        using OperationHealthProvider sut = new(store, new ParseFailureStatus(), serviceStatus);
        int changes = 0;
        sut.Changed += (_, _) => changes++;

        serviceStatus.ReportStopped(BackgroundServiceKind.SearchIndex);

        Assert.Equal(OperationHealth.Error, sut.Current);
        Assert.Equal(UiText.Format("Health_ServiceStopped", UiText.Get("Health_Service_SearchIndex")), sut.Detail);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Current_AfterAnError_ReportsErrorAndFiresChanged()
    {
        FakeStore store = new();
        ParseFailureStatus failureStatus = new();
        using OperationHealthProvider sut = new(store, failureStatus, new BackgroundServiceStatus());
        int changedCount = 0;
        sut.Changed += (_, _) => changedCount++;

        store.Add(LogLevel.Error, "Boom");

        Assert.Equal(OperationHealth.Error, sut.Current);
        Assert.Contains("Boom", sut.Detail, StringComparison.Ordinal);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void Current_AfterAWarning_ReportsWarning()
    {
        FakeStore store = new();
        ParseFailureStatus failureStatus = new();
        using OperationHealthProvider sut = new(store, failureStatus, new BackgroundServiceStatus());

        store.Add(LogLevel.Warning, "Spike");

        Assert.Equal(OperationHealth.Warning, sut.Current);
        Assert.Contains("Spike", sut.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_WithErrorAndWarningInTheWindow_ReportsError()
    {
        FakeStore store = new();
        ParseFailureStatus failureStatus = new();
        using OperationHealthProvider sut = new(store, failureStatus, new BackgroundServiceStatus());

        store.Add(LogLevel.Warning, "weiches Problem");
        store.Add(LogLevel.Error, "harter Fehler");

        Assert.Equal(OperationHealth.Error, sut.Current);
    }

    [Fact]
    public void Current_WithUnparsableFiles_ReportsWarningWithCount()
    {
        FakeStore store = new();
        ParseFailureStatus failureStatus = new();
        using OperationHealthProvider sut = new(store, failureStatus, new BackgroundServiceStatus());

        failureStatus.Update(3);

        Assert.Equal(OperationHealth.Warning, sut.Current);
        Assert.Contains("3 Dateien nicht verarbeitbar.", sut.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_WithASingleUnparsableFile_UsesSingularWording()
    {
        FakeStore store = new();
        ParseFailureStatus failureStatus = new();
        using OperationHealthProvider sut = new(store, failureStatus, new BackgroundServiceStatus());

        failureStatus.Update(1);

        Assert.Contains("1 Datei nicht verarbeitbar.", sut.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("normal", sut.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Current_WithUnparsableFilesAndAnError_KeepsErrorAndNamesBoth()
    {
        FakeStore store = new();
        ParseFailureStatus failureStatus = new();
        using OperationHealthProvider sut = new(store, failureStatus, new BackgroundServiceStatus());

        store.Add(LogLevel.Error, "Boom");
        failureStatus.Update(2);

        Assert.Equal(OperationHealth.Error, sut.Current);
        Assert.Contains("Boom", sut.Detail, StringComparison.Ordinal);
        Assert.Contains("2 Dateien nicht verarbeitbar.", sut.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_WhenUnparsableFilesAreResolved_ReturnsToHealthy()
    {
        FakeStore store = new();
        ParseFailureStatus failureStatus = new();
        using OperationHealthProvider sut = new(store, failureStatus, new BackgroundServiceStatus());
        failureStatus.Update(1);

        failureStatus.Update(0);

        Assert.Equal(OperationHealth.Healthy, sut.Current);
        Assert.Contains("normal", sut.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dispose_ThenAStatusChange_IsIgnored()
    {
        FakeStore store = new();
        ParseFailureStatus failureStatus = new();
        OperationHealthProvider sut = new(store, failureStatus, new BackgroundServiceStatus());
        sut.Dispose();

        failureStatus.Update(5);

        Assert.Equal(OperationHealth.Healthy, sut.Current);
    }

    /// <remarks>
    /// Das Fenster umfasst die jüngsten 200 Einträge. Mit 199 neueren Einträgen ist der Fehler
    /// noch darin, mit 200 ist er hinausgeschoben, und die Anzeige wird wieder grün.
    /// </remarks>
    [Theory]
    [InlineData(199, true)]
    [InlineData(200, false)]
    public void AnError_StaysUntilTwoHundredNewerEntriesPushItOut(int newerEntries, bool stillAnError)
    {
        FakeStore store = new();
        using OperationHealthProvider sut = new(store, new ParseFailureStatus(), new BackgroundServiceStatus());

        store.Add(LogLevel.Error, "Boom");
        for (int i = 0; i < newerEntries; i++)
        {
            store.Add(LogLevel.Information, "läuft");
        }

        Assert.Equal(stillAnError ? OperationHealth.Error : OperationHealth.Healthy, sut.Current);
    }

    /// <remarks>
    /// Zwei Threads protokollieren zugleich. Der erste hat seinen Ausschnitt gelesen, als noch
    /// alles in Ordnung war, und wird erst fertig, nachdem der zweite den Fehler gemeldet hat.
    /// Übernähme er seinen Stand danach, stünde die Anzeige auf grün, obwohl der Fehler im
    /// Fenster liegt. Die 200 ms geben dem zweiten Thread Gelegenheit, ohne Sperre
    /// durchzulaufen; mit Sperre wartet er, und der Test besteht unabhängig von der Last.
    /// </remarks>
    [Fact]
    public async Task Current_WhenAnEarlierEvaluationFinishesLate_KeepsTheLaterOne()
    {
        FakeStore store = new();
        using OperationHealthProvider sut = new(store, new ParseFailureStatus(), new BackgroundServiceStatus());
        using ManualResetEventSlim release = new();
        TaskCompletionSource firstReading = new(TaskCreationOptions.RunContinuationsAsynchronously);
        store.HoldNextSnapshot(firstReading, release);

        Task early = Task.Run(() => store.Add(LogLevel.Information, "früh"), TestContext.Current.CancellationToken);
        await firstReading.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);
        Task late = Task.Run(() => store.Add(LogLevel.Error, "Boom"), TestContext.Current.CancellationToken);
        _ = await Task.WhenAny(late, Task.Delay(200, TestContext.Current.CancellationToken)).ConfigureAwait(true);
        release.Set();
        await Task.WhenAll(early, late).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(OperationHealth.Error, sut.Current);
    }

    /// <summary>Protokollspeicher ohne Obergrenze; jeder Eintrag meldet sich sofort.</summary>
    private sealed class FakeStore : IMemoryLogStore
    {
        // Fester Zeitpunkt: Die Betriebsanzeige wertet ihn nicht aus.
        private static readonly DateTimeOffset Timestamp = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

        private readonly List<LogEntry> _entries = [];
        private readonly Lock _lock = new();
        private HeldReading? _held;

        // Die Betriebsanzeige liest die Kapazität nicht; der Wert erfüllt nur die Schnittstelle.
        public int Capacity => 1000;

        public event EventHandler<LogEntry>? EntryAdded;

        public void Add(LogLevel level, string message)
        {
            LogEntry entry = new(Timestamp, level, "Test", message, null);
            lock (_lock)
            {
                _entries.Add(entry);
            }
            EntryAdded?.Invoke(this, entry);
        }

        /// <summary>Hält den nächsten Leser nach dem Lesen an, bis <paramref name="release"/> gesetzt ist.</summary>
        public void HoldNextSnapshot(TaskCompletionSource reading, ManualResetEventSlim release) =>
            _held = new HeldReading(reading, release);

        public IReadOnlyList<LogEntry> Snapshot()
        {
            List<LogEntry> copy;
            lock (_lock)
            {
                copy = [.. _entries];
            }

            HeldReading? held = Interlocked.Exchange(ref _held, null);
            if (held is not null)
            {
                held.Reading.SetResult();
                _ = held.Release.Wait(TimeSpan.FromSeconds(5));
            }
            return copy;
        }

        /// <summary>Signal an den Test, dass gelesen wurde, und das Signal, das den Leser weiterlaufen lässt.</summary>
        private sealed record HeldReading(TaskCompletionSource Reading, ManualResetEventSlim Release);
    }
}
