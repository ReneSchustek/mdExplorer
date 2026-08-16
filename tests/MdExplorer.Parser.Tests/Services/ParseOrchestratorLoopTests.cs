using System.Data.Common;
using System.Runtime.CompilerServices;
using MdExplorer.Core.Abstractions;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Options;
using MdExplorer.Parser.Services;
using MdExplorer.Parser.Tests.Fakes;
using MdExplorer.Parser.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MEOptions = Microsoft.Extensions.Options.Options;

namespace MdExplorer.Parser.Tests.Services;

/// <summary>
/// Prüft die Polling-Schleife des <see cref="ParseOrchestrator"/> als Hintergrunddienst.
/// Die Schleife hat keinen Rückgabewert und keinen sichtbaren Zustand — ob sie nach einem
/// Fehlschlag weiterläuft, entscheidet aber darüber, ob geänderte Dateien überhaupt jemals
/// neu geparst werden. Die Zeit kommt aus einem <see cref="FakeTimeProvider"/>, damit kein
/// Test auf echte Sekunden wartet.
/// </summary>
public sealed class ParseOrchestratorLoopTests
{
    private static readonly TimeSpan Geduld = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ExecuteAsync_OnStart_ParsesBeforeTheFirstTick()
    {
        // Ohne den Vorab-Lauf bliebe frisch geänderter Inhalt eine volle Intervall-Länge
        // unsichtbar — der Nutzer sieht nach dem Start die alte Vorschau.
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            await u.StartAsync().ConfigureAwait(true);

            Assert.True(
                await u.WarteAufLaeufeAsync(1).ConfigureAwait(true),
                $"Erwartet: 1 Lauf, tatsächlich {u.Quelle.CallCount}.");
        }
    }

    [Fact]
    public async Task ExecuteAsync_OnTimerTick_ParsesAgain()
    {
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            await u.StartAsync().ConfigureAwait(true);
            _ = await u.WarteAufLaeufeAsync(1).ConfigureAwait(true);

            Assert.True(
                await u.WarteAufLaeufeAsync(2).ConfigureAwait(true),
                $"Erwartet: 2 Läufe, tatsächlich {u.Quelle.CallCount}.");
        }
    }

    [Fact]
    public async Task ExecuteAsync_OnRepeatedTicks_KeepsParsing()
    {
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            await u.StartAsync().ConfigureAwait(true);

            Assert.True(
                await u.WarteAufLaeufeAsync(4).ConfigureAwait(true),
                $"Erwartet: 4 Läufe, tatsächlich {u.Quelle.CallCount}.");
        }
    }

    [Fact]
    public async Task ExecuteAsync_OnStop_EndsWithoutFault()
    {
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            await u.StartAsync().ConfigureAwait(true);
            _ = await u.WarteAufLaeufeAsync(1).ConfigureAwait(true);

            await u.Sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            // Der Abbruch beim Herunterfahren ist der Normalfall und darf keinen Fehler hinterlassen.
            Assert.NotNull(u.Sut.ExecuteTask);
            Assert.True(u.Sut.ExecuteTask!.IsCompleted);
            Assert.False(u.Sut.ExecuteTask.IsFaulted);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenAPollFailsWithDbException_KeepsPolling()
    {
        // Eine Datenbank-Spitze darf den Parser nicht dauerhaft stilllegen.
        Schleifenumgebung u = new(new TestDbException("Datenbank belegt"));
        await using (u.ConfigureAwait(true))
        {
            await u.StartAsync().ConfigureAwait(true);

            Assert.True(
                await u.WarteAufLaeufeAsync(3).ConfigureAwait(true),
                $"Erwartet: 3 Läufe trotz Fehler, tatsächlich {u.Quelle.CallCount}.");
            Assert.False(u.Sut.ExecuteTask!.IsFaulted);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenAPollFailsWithInvalidOperationException_KeepsPolling()
    {
        Schleifenumgebung u = new(new InvalidOperationException("kaputter Zustand"));
        await using (u.ConfigureAwait(true))
        {
            await u.StartAsync().ConfigureAwait(true);

            Assert.True(
                await u.WarteAufLaeufeAsync(3).ConfigureAwait(true),
                $"Erwartet: 3 Läufe trotz Fehler, tatsächlich {u.Quelle.CallCount}.");
            Assert.False(u.Sut.ExecuteTask!.IsFaulted);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenAPollFailsWithArgumentException_KeepsPolling()
    {
        Schleifenumgebung u = new(new ArgumentException("ungültige Eingabe"));
        await using (u.ConfigureAwait(true))
        {
            await u.StartAsync().ConfigureAwait(true);

            Assert.True(
                await u.WarteAufLaeufeAsync(3).ConfigureAwait(true),
                $"Erwartet: 3 Läufe trotz Fehler, tatsächlich {u.Quelle.CallCount}.");
            Assert.False(u.Sut.ExecuteTask!.IsFaulted);
        }
    }

    [Fact]
    public async Task TryRunOnceAsync_OnDbException_DoesNotThrow()
    {
        Schleifenumgebung u = new(new TestDbException("Datenbank belegt"));
        await using (u.ConfigureAwait(true))
        {
            await u.Sut.TryRunOnceAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(1, u.Quelle.CallCount);
        }
    }

    [Fact]
    public async Task TryRunOnceAsync_WhenCancelled_Rethrows()
    {
        // Der Abbruch muss durchschlagen, damit die Schleife anhält statt weiterzupollen.
        Schleifenumgebung u = new(new OperationCanceledException());
        await using (u.ConfigureAwait(true))
        {
            using CancellationTokenSource abbruch = new();
            await abbruch.CancelAsync().ConfigureAwait(true);

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => u.Sut.TryRunOnceAsync(abbruch.Token)).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_OverSeveralTicks_PicksUpAFileAddedAfterTheStart()
    {
        // Der eigentliche Zweck der Schleife: was erst nach dem Start dazukommt, muss
        // ohne Neustart im Bestand landen.
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            await u.StartAsync().ConfigureAwait(true);
            _ = await u.WarteAufLaeufeAsync(1).ConfigureAwait(true);

            Guid id = u.FuegeQuelleHinzu("/r/spaet.md", "hash-spaet", "Text mit #spaet.");

            Assert.True(
                await u.WarteAufBedingungAsync(() => u.DocRepo.Snapshot.ContainsKey(id)).ConfigureAwait(true),
                "Die nach dem Start angelegte Datei wurde nicht geparst.");
        }
    }

    /// <summary>Hält Fakes und den Orchestrator für einen Testlauf zusammen.</summary>
    private sealed class Schleifenumgebung : IAsyncDisposable
    {
        private const int IntervallSekunden = 5;

        private readonly ServiceProvider _provider;
        private readonly FakeTimeProvider _zeit = new();
        private bool _gestartet;

        public Schleifenumgebung()
            : this(null)
        {
        }

        public Schleifenumgebung(Exception? fehler)
        {
            Quelle = new CountingSource(fehler);
            DocRepo.OnSaveChangesAsync = ct => TagRepo.SaveChangesAsync(ct);

            ServiceCollection dienste = new();
            _ = dienste.AddSingleton<IMarkdownSourceProvider>(Quelle);
            _ = dienste.AddSingleton<IMarkdownDocumentRepository>(DocRepo);
            _ = dienste.AddSingleton<ITagRepository>(TagRepo);
            _provider = dienste.BuildServiceProvider();

            MarkdigParser parser = new(
                new FrontmatterExtractor(),
                new TagExtractor(new FakeSettingsService()),
                new WikiLinkExtractor(),
                new TagNormalizer());

            Sut = new ParseOrchestrator(
                _provider.GetRequiredService<IServiceScopeFactory>(),
                FileSystem,
                parser,
                MEOptions.Create(new ParserOptions
                {
                    MaxParallelism = 2,
                    BatchSize = 100,
                    PollIntervalSeconds = IntervallSekunden,
                }),
                _zeit,
                NullLogger<ParseOrchestrator>.Instance);
        }

        public FakeFileSystem FileSystem { get; } = new();

        public CountingSource Quelle { get; }

        public FakeMarkdownDocumentRepository DocRepo { get; } = new();

        public FakeTagRepository TagRepo { get; } = new();

        public ParseOrchestrator Sut { get; }

        public async Task StartAsync()
        {
            _gestartet = true;
            await Sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        public Guid FuegeQuelleHinzu(string pfad, string inhaltsHash, string inhalt)
        {
            Guid id = Guid.NewGuid();
            string normalisiert = pfad.Replace('/', Path.DirectorySeparatorChar);
            FileSystem.AddFile(normalisiert, inhalt);
            lock (Quelle.Sperre)
            {
                Quelle.Sources.Add(new MarkdownSourceSnapshot(id, normalisiert, inhaltsHash));
            }
            return id;
        }

        public Task<bool> WarteAufLaeufeAsync(int anzahl) => WarteAufBedingungAsync(() => Quelle.CallCount >= anzahl);

        /// <summary>
        /// Wartet auf eine Bedingung und treibt dabei die Uhr weiter. Das Vorstellen geschieht in
        /// der Schleife statt einmalig, weil sonst ein Tick verloren gehen kann, solange die
        /// Schleife den Zeitgeber noch nicht erreicht hat — der Test würde dann je nach
        /// Maschinenlast unterschiedlich ausgehen.
        /// </summary>
        public async Task<bool> WarteAufBedingungAsync(Func<bool> bedingung)
        {
            ArgumentNullException.ThrowIfNull(bedingung);

            DateTimeOffset ende = DateTimeOffset.UtcNow + Geduld;
            while (DateTimeOffset.UtcNow < ende)
            {
                if (bedingung())
                {
                    return true;
                }
                _zeit.Advance(TimeSpan.FromSeconds(IntervallSekunden));
                await Task.Delay(5).ConfigureAwait(false);
            }
            return bedingung();
        }

        public async ValueTask DisposeAsync()
        {
            if (_gestartet)
            {
                await Sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
            }
            Sut.Dispose();
            await _provider.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Quelle, die ihre Durchläufe zählt und — anders als <see cref="FakeMarkdownSourceProvider"/> —
    /// denselben Fehler bei jedem Durchlauf wirft. Genau das braucht der Test der Schleife:
    /// ein einmaliger Fehler würde nicht zeigen, ob sie dauerhaft weiterläuft.
    /// </summary>
    private sealed class CountingSource : IMarkdownSourceProvider
    {
        private readonly Exception? _fehler;
        private int _aufrufe;

        public CountingSource(Exception? fehler) => _fehler = fehler;

        public List<MarkdownSourceSnapshot> Sources { get; } = [];

        public Lock Sperre { get; } = new();

        public int CallCount => Volatile.Read(ref _aufrufe);

        public async IAsyncEnumerable<MarkdownSourceSnapshot> EnumerateAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref _aufrufe);
            if (_fehler is not null)
            {
                await Task.Yield();
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_fehler).Throw();
            }

            MarkdownSourceSnapshot[] momentaufnahme;
            lock (Sperre)
            {
                momentaufnahme = [.. Sources];
            }
            foreach (MarkdownSourceSnapshot eintrag in momentaufnahme)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return eintrag;
                await Task.Yield();
            }
        }
    }

    /// <summary><see cref="DbException"/> ist abstrakt — für den Fehlerpfad braucht es eine eigene Ausprägung.</summary>
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
