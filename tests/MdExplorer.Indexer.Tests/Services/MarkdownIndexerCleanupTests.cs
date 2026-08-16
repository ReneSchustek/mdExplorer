using MdExplorer.Indexer.Tests.Fakes;

namespace MdExplorer.Indexer.Tests.Services;

/// <summary>
/// Hält fest, wann Einträge entfernt werden — und wann ausdrücklich nicht.
/// </summary>
/// <remarks>
/// Bis zum 16.08.2026 hing der Aufräumdurchgang am Erfolg des Schreibens: Eine
/// Datenbank-Spitze ließ ihn ganz ausfallen, der Wächter beendete den Dienst, und danach lief
/// kein Abgleich mehr — weder für neue Dateien noch für verschwundene. Das ist die
/// mutmaßliche Ursache der 5.975 verwaisten Einträge, die die Auswertung der Arbeitsdatenbank
/// am selben Tag fand.
/// </remarks>
public sealed class MarkdownIndexerCleanupTests
{
    private static readonly DateTime FixedWrite = new(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc);

    /// <remarks>
    /// Der Kern: Ein gescheiterter Stapel kostet den Stapel, nicht den Durchgang. Die
    /// verschwundene Datei muss trotzdem aus dem Bestand fallen.
    /// </remarks>
    [Fact]
    public async Task AFailedBatchDoesNotStopTheCleanup()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create(initialScanBatchSize: 1);
        harness.FileSystem.AddFile(@"C:\Wurzel\bleibt.md", "Inhalt", FixedWrite);
        harness.FileSystem.AddFile(@"C:\Wurzel\verschwindet.md", "Inhalt", FixedWrite);
        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(2, harness.Repository.Snapshot.Count);

        harness.FileSystem.RemoveFile(@"C:\Wurzel\verschwindet.md");
        harness.FileSystem.AddFile(@"C:\Wurzel\neu.md", "Inhalt", FixedWrite);
        harness.Repository.ThrowOnNextSave = new FakeDbException();

        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.DoesNotContain(@"C:\Wurzel\verschwindet.md", harness.Repository.Snapshot.Keys);
        Assert.Contains(@"C:\Wurzel\bleibt.md", harness.Repository.Snapshot.Keys);
    }

    /// <remarks>
    /// Die Gegenbedingung, ohne die der Umbau gefährlich wäre: Was nicht gelesen wurde, sieht
    /// aus wie gelöscht. Ein halb gelesener Baum darf deshalb nichts entfernen.
    /// </remarks>
    [Fact]
    public async Task AHalfReadTreeRemovesNothing()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\eins.md", "Inhalt", FixedWrite);
        harness.FileSystem.AddFile(@"C:\Wurzel\zwei.md", "Inhalt", FixedWrite);
        harness.FileSystem.AddFile(@"C:\Wurzel\drei.md", "Inhalt", FixedWrite);
        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(3, harness.Repository.Snapshot.Count);

        // Der Zugriff bricht nach der ersten Datei ab — die beiden anderen sind nicht
        // verschwunden, sie wurden nur nicht gesehen.
        harness.FileSystem.ThrowAfterEnumerating = 1;

        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(3, harness.Repository.Snapshot.Count);
    }

    /// <remarks>
    /// Und die Probe aufs Exempel: Ist der Baum wieder vollständig lesbar, greift der
    /// Aufräumdurchgang beim nächsten Lauf.
    /// </remarks>
    [Fact]
    public async Task TheNextCompleteRunCleansUpAgain()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\eins.md", "Inhalt", FixedWrite);
        harness.FileSystem.AddFile(@"C:\Wurzel\zwei.md", "Inhalt", FixedWrite);
        harness.FileSystem.AddFile(@"C:\Wurzel\drei.md", "Inhalt", FixedWrite);
        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        // Eine Datei verschwindet, aber der Baum lässt sich nur zum Teil lesen: nichts entfernen.
        harness.FileSystem.RemoveFile(@"C:\Wurzel\drei.md");
        harness.FileSystem.ThrowAfterEnumerating = 1;
        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(3, harness.Repository.Snapshot.Count);

        // Beim nächsten vollständigen Lauf greift der Aufräumdurchgang wieder.
        harness.FileSystem.ThrowAfterEnumerating = null;
        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, harness.Repository.Snapshot.Count);
        Assert.DoesNotContain(@"C:\Wurzel\drei.md", harness.Repository.Snapshot.Keys);
    }
}
