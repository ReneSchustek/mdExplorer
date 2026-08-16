namespace MdExplorer.Indexer.Tests.Services;

/// <summary>
/// Prüft die zwei Selbstheilungs-Mechanismen des Indexers: den periodischen Abgleich und
/// das Wiederholen beim Lesen. Beide fangen Fälle auf, die die Dateisystem-Überwachung
/// nicht meldet — eine Datei, die während einer Netzwerktrennung entsteht, und eine, die
/// im Moment des Lesens von einem anderen Programm gesperrt ist. Ohne sie fehlt die Datei
/// dauerhaft im Bestand, ohne dass irgendwo ein Fehler sichtbar würde.
/// </summary>
public sealed class MarkdownIndexerResyncTests
{
    private static readonly DateTime FesteZeit = new(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Abgleichintervall = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Geduld = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ResyncLoop_PicksUpAFileTheWatcherNeverReported()
    {
        IndexerTestHarness harness = IndexerTestHarness.Create(resyncInterval: Abgleichintervall);
        await using (harness.ConfigureAwait(true))
        {
            await harness.Indexer.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            string pfad = Path.Combine(IndexerTestHarness.DefaultRoot, "still-entstanden.md");

            // Direkt am Dateisystem vorbei an der Überwachung — genau der Fall, den der
            // periodische Abgleich auffangen soll.
            harness.FileSystem.AddFile(pfad, "# Still entstanden", FesteZeit);

            Assert.True(
                await AdvanceUntilAsync(harness, () => harness.Repository.Snapshot.ContainsKey(pfad)).ConfigureAwait(true),
                "Der periodische Abgleich hat die Datei nicht aufgenommen.");
            await harness.Indexer.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task ResyncLoop_RunsRepeatedly()
    {
        IndexerTestHarness harness = IndexerTestHarness.Create(resyncInterval: Abgleichintervall);
        await using (harness.ConfigureAwait(true))
        {
            await harness.Indexer.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            string erste = Path.Combine(IndexerTestHarness.DefaultRoot, "erste.md");
            harness.FileSystem.AddFile(erste, "# Erste", FesteZeit);
            _ = await AdvanceUntilAsync(harness, () => harness.Repository.Snapshot.ContainsKey(erste)).ConfigureAwait(true);

            string zweite = Path.Combine(IndexerTestHarness.DefaultRoot, "zweite.md");
            harness.FileSystem.AddFile(zweite, "# Zweite", FesteZeit);

            Assert.True(
                await AdvanceUntilAsync(harness, () => harness.Repository.Snapshot.ContainsKey(zweite)).ConfigureAwait(true),
                "Der Abgleich lief nur ein einziges Mal.");
            await harness.Indexer.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task ResyncLoop_OnStop_EndsWithoutFault()
    {
        IndexerTestHarness harness = IndexerTestHarness.Create(resyncInterval: Abgleichintervall);
        await using (harness.ConfigureAwait(true))
        {
            await harness.Indexer.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            await harness.Indexer.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.NotNull(harness.Indexer.ExecuteTask);
            Assert.True(harness.Indexer.ExecuteTask!.IsCompleted);
            Assert.False(harness.Indexer.ExecuteTask.IsFaulted);
        }
    }

    [Fact]
    public async Task ResyncLoop_WhenTheIntervalIsZero_DoesNotRun()
    {
        // Abgleich abgeschaltet: Eine an der Überwachung vorbei entstandene Datei bleibt
        // dann bewusst unbemerkt — sonst wäre die Einstellung wirkungslos.
        IndexerTestHarness harness = IndexerTestHarness.Create(resyncInterval: TimeSpan.Zero);
        await using (harness.ConfigureAwait(true))
        {
            // Eine Datei vor dem Start anlegen und ihr Erscheinen abwarten: Damit ist der
            // Erstlauf nachweislich durch. Ohne diesen Punkt würde die zweite Datei je nach
            // Maschinenlast noch vom Erstlauf erfasst und der Test schlüge grundlos fehl.
            string vorher = Path.Combine(IndexerTestHarness.DefaultRoot, "vor-dem-start.md");
            harness.FileSystem.AddFile(vorher, "# Vor dem Start", FesteZeit);
            await harness.Indexer.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.True(
                await AdvanceUntilAsync(harness, () => harness.Repository.Snapshot.ContainsKey(vorher)).ConfigureAwait(true),
                "Der Erstlauf hat die vorhandene Datei nicht aufgenommen.");

            string pfad = Path.Combine(IndexerTestHarness.DefaultRoot, "unbemerkt.md");
            harness.FileSystem.AddFile(pfad, "# Unbemerkt", FesteZeit);

            for (int schritt = 0; schritt < 20; schritt++)
            {
                harness.TimeProvider.Advance(Abgleichintervall);
                await Task.Delay(5, TestContext.Current.CancellationToken).ConfigureAwait(true);
            }

            Assert.False(harness.Repository.Snapshot.ContainsKey(pfad));
            await harness.Indexer.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task InitialScan_WhenTheFileIsBrieflyLocked_RetriesAndIndexesIt()
    {
        IndexerTestHarness harness = IndexerTestHarness.Create();
        await using (harness.ConfigureAwait(true))
        {
            string pfad = Path.Combine(IndexerTestHarness.DefaultRoot, "gesperrt.md");
            harness.FileSystem.AddFile(pfad, "# Gesperrt", FesteZeit);
            harness.FileSystem.FailOpenRead(pfad, times: 2);

            Task lauf = harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken);
            await TreibeUhrBisFertigAsync(harness, lauf).ConfigureAwait(true);

            Assert.True(harness.Repository.Snapshot.ContainsKey(pfad), "Die kurzzeitig gesperrte Datei fehlt im Bestand.");
        }
    }

    [Fact]
    public async Task InitialScan_WhenTheFileStaysLocked_SkipsItWithoutFailingTheScan()
    {
        // Nach dem Wiederholungsbudget wird die Datei übersprungen. Der Durchlauf muss
        // trotzdem zu Ende laufen, sonst blockiert eine einzige Datei den ganzen Bestand.
        IndexerTestHarness harness = IndexerTestHarness.Create();
        await using (harness.ConfigureAwait(true))
        {
            string gesperrt = Path.Combine(IndexerTestHarness.DefaultRoot, "dauerhaft-gesperrt.md");
            string offen = Path.Combine(IndexerTestHarness.DefaultRoot, "lesbar.md");
            harness.FileSystem.AddFile(gesperrt, "# Gesperrt", FesteZeit);
            harness.FileSystem.AddFile(offen, "# Lesbar", FesteZeit);
            harness.FileSystem.FailOpenRead(gesperrt, times: 99);

            Task lauf = harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken);
            await TreibeUhrBisFertigAsync(harness, lauf).ConfigureAwait(true);

            Assert.False(harness.Repository.Snapshot.ContainsKey(gesperrt));
            Assert.True(harness.Repository.Snapshot.ContainsKey(offen), "Die lesbare Datei wurde mit übersprungen.");
        }
    }

    /// <summary>
    /// Stellt die Uhr schrittweise vor, bis die Bedingung erfüllt ist. Der Abgleich läuft im
    /// Hintergrund; ein einmaliges Vorstellen kann den Zeitgeber verfehlen, solange die
    /// Schleife ihn noch nicht erreicht hat.
    /// </summary>
    private static async Task<bool> AdvanceUntilAsync(IndexerTestHarness harness, Func<bool> bedingung)
    {
        DateTimeOffset ende = DateTimeOffset.UtcNow + Geduld;
        while (DateTimeOffset.UtcNow < ende)
        {
            if (bedingung())
            {
                return true;
            }
            harness.TimeProvider.Advance(Abgleichintervall);
            await Task.Delay(5).ConfigureAwait(false);
        }
        return bedingung();
    }

    /// <summary>Treibt die Uhr weiter, bis der Durchlauf fertig ist — die Wartezeit zwischen
    /// zwei Leseversuchen läuft ebenfalls über den Zeitgeber.</summary>
    private static async Task TreibeUhrBisFertigAsync(IndexerTestHarness harness, Task lauf)
    {
        DateTimeOffset ende = DateTimeOffset.UtcNow + Geduld;
        while (!lauf.IsCompleted && DateTimeOffset.UtcNow < ende)
        {
            harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(200));
            _ = await Task.WhenAny(lauf, Task.Delay(5)).ConfigureAwait(false);
        }
        await lauf.ConfigureAwait(false);
    }
}
