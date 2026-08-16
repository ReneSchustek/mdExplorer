using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Prüft den Quellenanbieter des Graphen gegen eine echte SQLite.
/// </summary>
/// <remarks>
/// <para>
/// Der Grund für eine echte Datenbank statt eines Doubles steht in
/// <see cref="GraphSourceProvider.LoadNeighborhoodDocumentsAsync"/>: Der Vorfilter wird von EF
/// Core in ein SQL-<c>LIKE</c> übersetzt. Ein Double würde bestätigen, was ich mir dabei
/// gedacht habe — nicht, was SQLite daraus macht. Genau dazwischen liegt der Fehler, den
/// dieser Weg verhindern soll.
/// </para>
/// <para>
/// Angelegt am 16.08.2026 zusammen mit dem Umbau, der den Graphen nur noch die Nachbarschaft
/// eines Knotens laden lässt statt den ganzen Bestand.
/// </para>
/// </remarks>
public sealed class GraphSourceProviderTests : IAsyncDisposable
{
    private static readonly DateTime FixedUtc = new(2026, 8, 16, 9, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly MdExplorerDbContext _dbContext;
    private readonly GraphSourceProvider _sut;

    public GraphSourceProviderTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        DbContextOptions<MdExplorerDbContext> options = new DbContextOptionsBuilder<MdExplorerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
        _dbContext = new MdExplorerDbContext(options);
        _ = _dbContext.Database.EnsureCreated();
        _sut = new GraphSourceProvider(_dbContext);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    /// <remarks>
    /// Der Kern des Umbaus: Von 40 Dokumenten dürfen nur die wenigen zurückkommen, die den
    /// Knoten überhaupt betreffen. Die Zahl steht in der Zusicherung — sonst wäre der Test
    /// auch dann grün, wenn wieder alles geladen würde.
    /// </remarks>
    [Fact]
    public async Task LoadNeighborhoodDocumentsAsync_ReturnsOnlyTheNeighborhood()
    {
        Guid mitte = IdOf(1);
        Guid zeigtHin = IdOf(2);
        Guid unbeteiligt = IdOf(3);

        await SeedDocumentAsync(mitte, "mitte", ["woanders-hin"]).ConfigureAwait(true);
        await SeedDocumentAsync(zeigtHin, "zeigt-hin", ["mitte"]).ConfigureAwait(true);
        await SeedDocumentAsync(unbeteiligt, "unbeteiligt", ["dritter-ort"]).ConfigureAwait(true);
        for (int i = 4; i <= 40; i++)
        {
            await SeedDocumentAsync(IdOf(i), "eintrag-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), []).ConfigureAwait(true);
        }

        IReadOnlyList<GraphSourceDocument> result =
            await _sut.LoadNeighborhoodDocumentsAsync(mitte, "mitte", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, document => document.MarkdownFileId == mitte);
        Assert.Contains(result, document => document.MarkdownFileId == zeigtHin);
        Assert.DoesNotContain(result, document => document.MarkdownFileId == unbeteiligt);
    }

    /// <remarks>
    /// Das eigene Dokument muss auch dann dabei sein, wenn niemand auf es zeigt — sonst
    /// verlöre der Knoten seine ausgehenden Verweise.
    /// </remarks>
    [Fact]
    public async Task LoadNeighborhoodDocumentsAsync_AlwaysIncludesTheNodeItself()
    {
        Guid allein = IdOf(1);
        await SeedDocumentAsync(allein, "allein", ["irgendwohin"]).ConfigureAwait(true);
        await SeedDocumentAsync(IdOf(2), "anderer", ["nicht-allein"]).ConfigureAwait(true);

        IReadOnlyList<GraphSourceDocument> result =
            await _sut.LoadNeighborhoodDocumentsAsync(allein, "allein", CancellationToken.None).ConfigureAwait(true);

        GraphSourceDocument single = Assert.Single(result);
        Assert.Equal(allein, single.MarkdownFileId);
        Assert.Contains("irgendwohin", single.OutlinksJson, StringComparison.Ordinal);
    }

    /// <remarks>
    /// <para>
    /// Die unangenehme Stelle: In einem <c>LIKE</c> sind <c>%</c> und <c>_</c> Platzhalter.
    /// Käme ein Slug mit Unterstrich durch, träfe <c>a_b</c> auch <c>axb</c> — der Graph zöge
    /// eine Kante, die im Dokument nicht steht.
    /// </para>
    /// <para>
    /// Die Slug-Bildung lässt beides nicht durch; hier steht die Gegenprobe, damit es dabei
    /// bleibt. Und selbst wenn: Der Vorfilter darf zu viel liefern, weil die Auswertung
    /// danach jeden Treffer gegen die tatsächliche Verweis-Liste hält. Dieser Test hält fest,
    /// dass genau diese zweite Prüfung nötig ist und nicht wegoptimiert werden darf.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task LoadNeighborhoodDocumentsAsync_MayOverdeliverButNeverUnderdelivers()
    {
        Guid gesucht = IdOf(1);
        Guid falscherTreffer = IdOf(2);

        await SeedDocumentAsync(gesucht, "a-b", []).ConfigureAwait(true);
        await SeedDocumentAsync(falscherTreffer, "sonst", ["a-b-und-mehr"]).ConfigureAwait(true);

        IReadOnlyList<GraphSourceDocument> result =
            await _sut.LoadNeighborhoodDocumentsAsync(gesucht, "a-b", CancellationToken.None).ConfigureAwait(true);

        // Das eigene Dokument ist immer dabei; der Nachbar mit dem längeren Ziel nicht,
        // weil die Anführungszeichen im Suchmuster das Ziel begrenzen.
        GraphSourceDocument single = Assert.Single(result);
        Assert.Equal(gesucht, single.MarkdownFileId);
    }

    [Fact]
    public async Task LoadNeighborhoodDocumentsAsync_OnBlankSlug_Throws()
    {
        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.LoadNeighborhoodDocumentsAsync(IdOf(1), "   ", CancellationToken.None)).ConfigureAwait(true);
    }

    /// <remarks>
    /// Die drei kurzen Spalten sind der ganze Zweck der Methode — sie ersetzt das Laden des
    /// vollen Schnappschusses. Der Test hält fest, dass sie die Dateien vollständig und in
    /// stabiler Reihenfolge liefert.
    /// </remarks>
    [Fact]
    public async Task LoadFilesAsync_ReturnsEveryFileOrderedById()
    {
        await SeedDocumentAsync(IdOf(2), "zweite", []).ConfigureAwait(true);
        await SeedDocumentAsync(IdOf(1), "erste", []).ConfigureAwait(true);

        IReadOnlyList<GraphSourceFile> result = await _sut.LoadFilesAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, result.Count);
        Assert.Equal(IdOf(1), result[0].Id);
        Assert.Equal("erste", result[0].FileNameWithoutExtension);
        Assert.Equal("unter/zweite.md", result[1].RelativePath);
    }

    private static Guid IdOf(int index)
        => new(index.ToString("D8", System.Globalization.CultureInfo.InvariantCulture) + "-0000-0000-0000-000000000000");

    private async Task SeedDocumentAsync(Guid id, string name, IReadOnlyList<string> outlinkSlugs)
    {
        MarkdownFile file = new()
        {
            Id = id,
            AbsolutePath = @"C:\notes\unter\" + name + ".md",
            RelativePath = "unter/" + name + ".md",
            FileNameWithoutExtension = name,
            SizeBytes = 0,
            LastWriteTimeUtc = FixedUtc,
            ContentHash = "hash-" + name,
            IndexedAtUtc = FixedUtc,
        };
        _ = await _dbContext.Set<MarkdownFile>().AddAsync(file).ConfigureAwait(true);

        MarkdownDocument document = new()
        {
            Id = Guid.NewGuid(),
            MarkdownFileId = id,
            SourceContentHash = "hash-" + name,
            FrontmatterJson = "{}",
            OutlinksJson = System.Text.Json.JsonSerializer.Serialize(outlinkSlugs),
            ParsedAtUtc = FixedUtc,
        };
        _ = await _dbContext.Set<MarkdownDocument>().AddAsync(document).ConfigureAwait(true);
        _ = await _dbContext.SaveChangesAsync().ConfigureAwait(true);
    }
}
