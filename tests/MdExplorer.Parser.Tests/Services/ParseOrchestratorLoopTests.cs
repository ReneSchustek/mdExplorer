using System.Data.Common;
using MdExplorer.Data;
using MdExplorer.Parser.Services;
using MdExplorer.Parser.Tests.Fakes;
using MdExplorer.Parser.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Parser.Tests.Services;

/// <summary>
/// Prüft die Polling-Schleife des <see cref="ParseOrchestrator"/> als Hintergrunddienst.
/// Die Schleife hat keinen Rückgabewert und keinen sichtbaren Zustand. Ob sie nach einem
/// Fehlschlag weiterläuft, entscheidet aber darüber, ob geänderte Dateien überhaupt jemals
/// neu geparst werden. Getaktet wird über eine feste Uhr: Jeder Schritt wartet auf das Ende
/// des Durchlaufs im Protokoll und stellt die Uhr erst dann um ein Intervall weiter.
/// </summary>
public sealed class ParseOrchestratorLoopTests
{
    // Die Ereignis-Ids von LogPollFailed und LogPollFailedAgain im ParseOrchestrator.
    private const int PollFailedEventId = 207;

    private const int PollFailedAgainEventId = 213;

    [Fact]
    public async Task ExecuteAsync_OnStart_ParsesBeforeTheFirstTick()
    {
        // Ohne den Vorab-Lauf bliebe frisch geänderter Inhalt eine volle Intervall-Länge
        // unsichtbar; der Nutzer sieht nach dem Start die alte Vorschau.
        OrchestratorHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            await harness.StartAsync().ConfigureAwait(true);

            Assert.Equal(1, harness.Source.CallCount);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ExecuteAsync_OnEachTick_ParsesAgain(int ticks)
    {
        OrchestratorHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            await harness.StartAsync().ConfigureAwait(true);

            for (int tick = 0; tick < ticks; tick++)
            {
                await harness.RunNextTickAsync().ConfigureAwait(true);
            }

            Assert.Equal(ticks + 1, harness.Source.CallCount);
        }
    }

    [Fact]
    public async Task ExecuteAsync_OnStop_EndsWithoutFault()
    {
        OrchestratorHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            await harness.StartAsync().ConfigureAwait(true);

            await harness.Sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            // Der Abbruch beim Herunterfahren ist der Normalfall und darf keinen Fehler hinterlassen.
            Task execution = Assert.IsType<Task>(harness.Sut.ExecuteTask, exactMatch: false);
            Assert.True(execution.IsCompleted);
            Assert.False(execution.IsFaulted);
        }
    }

    [Theory]
    [InlineData("database")]
    [InlineData("invalid-operation")]
    [InlineData("argument")]
    public async Task ExecuteAsync_WhenEveryPollFails_KeepsPolling(string kind)
    {
        // Eine Datenbank-Spitze oder ein kaputter Zustand darf den Parser nicht dauerhaft
        // stilllegen.
        OrchestratorHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            harness.Source.ThrowOnEveryEnumeration = kind switch
            {
                "database" => new TestDbException("Datenbank belegt"),
                "invalid-operation" => new InvalidOperationException("kaputter Zustand"),
                _ => new ArgumentException("ungültige Eingabe"),
            };
            await harness.StartAsync().ConfigureAwait(true);

            await harness.RunNextTickAsync().ConfigureAwait(true);
            await harness.RunNextTickAsync().ConfigureAwait(true);

            Assert.Equal(3, harness.Source.CallCount);
            Assert.False(harness.Sut.ExecuteTask?.IsFaulted ?? true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenEveryPollFails_LogsTheStackTraceOnlyOnce()
    {
        // Eine dauerhaft gesperrte Datenbank schrieb sonst alle fünf Sekunden einen vollen
        // Stapel ins Protokoll und verdrängte jede andere Meldung.
        OrchestratorHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            harness.Source.ThrowOnEveryEnumeration = new TestDbException("Datenbank belegt");
            await harness.StartAsync().ConfigureAwait(true);

            await harness.RunNextTickAsync().ConfigureAwait(true);
            await harness.RunNextTickAsync().ConfigureAwait(true);

            Assert.Equal(1, harness.Logger.Entries.Count(entry => entry.EventId == PollFailedEventId && entry.Exception is not null));
            Assert.Equal(2, harness.Logger.Entries.Count(entry => entry.EventId == PollFailedAgainEventId && entry.Level == LogLevel.Debug));
        }
    }

    [Fact]
    public async Task ExecuteAsync_AfterAPollRecovers_LogsTheNextFailureInFull()
    {
        // Ein Fehler nach einem erfolgreichen Durchlauf ist ein neues Ereignis.
        OrchestratorHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            harness.Source.ThrowOnNextEnumeration = new TestDbException("Datenbank belegt");
            await harness.StartAsync().ConfigureAwait(true);
            await harness.RunNextTickAsync().ConfigureAwait(true);

            harness.Source.ThrowOnNextEnumeration = new TestDbException("Datenbank belegt");
            await harness.RunNextTickAsync().ConfigureAwait(true);

            Assert.Equal(2, harness.Logger.Entries.Count(entry => entry.EventId == PollFailedEventId && entry.Exception is not null));
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenSavingFailsEveryTime_KeepsPolling()
    {
        // So meldet das echte Repository einen verletzten Schlüssel oder eine belegte Datenbank
        // beim Speichern. Endete der Dienst daran, bliebe der Index bis zum Neustart stehen.
        OrchestratorHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            _ = harness.AddSource("/r/a.md", "h1", "Text mit #schlagwort");
            harness.DocRepo.OnSaveChangesAsync = _ => throw new DatabaseOperationException(
                "Speichern fehlgeschlagen.", new DbUpdateException("UNIQUE constraint failed: Tags.Slug"));
            await harness.StartAsync().ConfigureAwait(true);

            await harness.RunNextTickAsync().ConfigureAwait(true);
            await harness.RunNextTickAsync().ConfigureAwait(true);

            Assert.Equal(3, harness.Source.CallCount);
            Assert.False(harness.Sut.ExecuteTask?.IsFaulted ?? true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_OverSeveralTicks_PicksUpAFileAddedAfterTheStart()
    {
        // Der eigentliche Zweck der Schleife: was erst nach dem Start dazukommt, muss
        // ohne Neustart im Bestand landen.
        OrchestratorHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            await harness.StartAsync().ConfigureAwait(true);

            Guid id = harness.AddSource("/r/nachtrag.md", "hash-nachtrag", "Text mit #nachtrag.");
            await harness.RunNextTickAsync().ConfigureAwait(true);

            Assert.True(harness.DocRepo.Snapshot.ContainsKey(id), "Die nach dem Start angelegte Datei wurde nicht geparst.");
        }
    }

    [Fact]
    public async Task TryRunOnceAsync_OnDbException_DoesNotThrow()
    {
        using OrchestratorHarness harness = new();
        harness.Source.ThrowOnEveryEnumeration = new TestDbException("Datenbank belegt");

        await harness.Sut.TryRunOnceAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, harness.Source.CallCount);
    }

    [Fact]
    public async Task TryRunOnceAsync_WhenCancelled_Rethrows()
    {
        // Der Abbruch muss durchschlagen, damit die Schleife anhält statt weiterzupollen.
        using OrchestratorHarness harness = new();
        harness.Source.ThrowOnEveryEnumeration = new OperationCanceledException();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync().ConfigureAwait(true);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Sut.TryRunOnceAsync(cancellation.Token)).ConfigureAwait(true);
    }

    /// <summary><see cref="DbException"/> ist abstrakt; für den Fehlerpfad braucht es eine eigene Ausprägung.</summary>
    private sealed class TestDbException : DbException
    {
        public TestDbException()
        {
        }

        public TestDbException(string message)
            : base(message)
        {
        }

        public TestDbException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
