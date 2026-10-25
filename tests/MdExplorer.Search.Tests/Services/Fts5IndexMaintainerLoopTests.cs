using System.Data.Common;
using System.Text;
using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Options;
using MdExplorer.Search.Services;
using MdExplorer.Search.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// Prüft den Hintergrunddienst-Anteil des <see cref="Fts5IndexMaintainer"/>: die Polling-Schleife
/// und den Weg von der Quelldatei bis zum Index-Eintrag. Die Schleife entscheidet darüber, ob der
/// Index überhaupt jemals nachgezogen wird, und ob ein einzelner Fehlschlag den Dienst beendet oder
/// nur einen Durchlauf kostet. Beides ist ohne Test nicht zu sehen, weil es keinen Rückgabewert hat.
/// Die Zeit kommt aus einem <see cref="FakeTimeProvider"/>, damit kein Test auf echte Sekunden wartet.
/// </summary>
public sealed class Fts5IndexMaintainerLoopTests
{
    // Echte Wartezeit für die Hintergrundschleife. Sie greift nur, wenn etwas hängt, und muss
    // dann unter paralleler Testlast reichen.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ExecuteAsync_OnAnUnexpectedException_StopsAndReportsItsStop()
    {
        // Was die Takt-Behandlung nicht kennt, beendet den Dienst geordnet. Die Betriebsanzeige muss
        // das erfahren, sonst liefert die Suche bis zum Neustart still veraltete Treffer.
        LoopHarness harness = new(new NotSupportedException("unerwartet"));
        await using (harness.ConfigureAwait(true))
        {
            await harness.StartAsync().ConfigureAwait(true);
            Task execution = harness.Sut.ExecuteTask ?? throw new InvalidOperationException("Der Dienst lief nicht an.");
            await execution.WaitAsync(Patience, TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.False(execution.IsFaulted);
            Assert.Equal([BackgroundServiceKind.SearchIndex], harness.ServiceStatus.Stopped);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WithAnUnchangedStock_SkipsTheFullLoad()
    {
        // Ein Takt ohne Änderung kostete über 7.700 Notizen 123 ms und 26 MB, alle fünf Sekunden.
        LoopHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            harness.Source.FixedVersion = new SearchSourceVersion(1, new DateTime(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc), 1, null);
            _ = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            _ = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Equal(1, harness.Source.CallCount);

            harness.Source.FixedVersion = harness.Source.FixedVersion.Value with { DocumentCount = 2 };
            _ = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Equal(2, harness.Source.CallCount);
        }
    }

    [Fact]
    public async Task ExecuteAsync_OnStart_SynchronizesBeforeTheFirstTick()
    {
        // Ohne den Vorab-Lauf bliebe der Index nach dem Programmstart eine volle
        // Intervall-Länge veraltet, sichtbar als "Suche findet die gerade erst
        // geöffnete Datei nicht".
        LoopHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            await harness.StartAsync().ConfigureAwait(true);

            Assert.True(
                await harness.WaitForRunsAsync(1).ConfigureAwait(true),
                $"Erwartet: 1 Lauf, tatsächlich {harness.Source.CallCount}.");
        }
    }

    [Fact]
    public async Task ExecuteAsync_OnTimerTick_SynchronizesAgain()
    {
        LoopHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            await harness.StartAsync().ConfigureAwait(true);
            _ = await harness.WaitForRunsAsync(1).ConfigureAwait(true);

            Assert.True(
                await harness.WaitForRunsAsync(2).ConfigureAwait(true),
                $"Erwartet: 2 Läufe, tatsächlich {harness.Source.CallCount}.");
        }
    }

    [Fact]
    public async Task ExecuteAsync_OnRepeatedTicks_KeepsSynchronizing()
    {
        LoopHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            await harness.StartAsync().ConfigureAwait(true);

            Assert.True(
                await harness.WaitForRunsAsync(4).ConfigureAwait(true),
                $"Erwartet: 4 Läufe, tatsächlich {harness.Source.CallCount}.");
        }
    }

    [Fact]
    public async Task ExecuteAsync_OnStop_EndsWithoutFault()
    {
        LoopHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            await harness.StartAsync().ConfigureAwait(true);
            _ = await harness.WaitForRunsAsync(1).ConfigureAwait(true);

            await harness.Sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            // Ein Abbruch ist der Normalfall beim Herunterfahren und darf keinen Fehler hinterlassen.
            Assert.NotNull(harness.Sut.ExecuteTask);
            Assert.True(harness.Sut.ExecuteTask!.IsCompleted);
            Assert.False(harness.Sut.ExecuteTask.IsFaulted);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenSynchronizationFailsWithDbException_KeepsPolling()
    {
        // Eine Datenbank-Spitze darf den Dienst nicht beenden, sonst bleibt der Index bis zum
        // nächsten Programmstart stehen, und niemand bemerkt es.
        LoopHarness harness = new(new TestDbException("Datenbank belegt"));
        await using (harness.ConfigureAwait(true))
        {
            await harness.StartAsync().ConfigureAwait(true);

            Assert.True(
                await harness.WaitForRunsAsync(3).ConfigureAwait(true),
                $"Erwartet: 3 Läufe trotz Fehler, tatsächlich {harness.Source.CallCount}.");
            Assert.False(harness.Sut.ExecuteTask!.IsFaulted);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenSynchronizationFailsWithArgumentException_KeepsPolling()
    {
        LoopHarness harness = new(new ArgumentException("kaputter Zustand"));
        await using (harness.ConfigureAwait(true))
        {
            await harness.StartAsync().ConfigureAwait(true);

            Assert.True(
                await harness.WaitForRunsAsync(3).ConfigureAwait(true),
                $"Erwartet: 3 Läufe trotz Fehler, tatsächlich {harness.Source.CallCount}.");
            Assert.False(harness.Sut.ExecuteTask!.IsFaulted);
        }
    }

    [Fact]
    public async Task TrySynchronizeAsync_OnDbException_DoesNotThrow()
    {
        LoopHarness harness = new(new TestDbException("Datenbank belegt"));
        await using (harness.ConfigureAwait(true))
        {
            await harness.Sut.TrySynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(1, harness.Source.CallCount);
        }
    }

    [Fact]
    public async Task TrySynchronizeAsync_WhenCancelled_Rethrows()
    {
        // Der Abbruch muss durchschlagen, damit die Schleife anhält statt weiterzupollen.
        LoopHarness harness = new(new OperationCanceledException());
        await using (harness.ConfigureAwait(true))
        {
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync().ConfigureAwait(true);

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => harness.Sut.TrySynchronizeAsync(cancellation.Token)).ConfigureAwait(true);
        }
    }
}
