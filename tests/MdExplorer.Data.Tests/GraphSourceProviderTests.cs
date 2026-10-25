using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Der Quellenanbieter des Graphen lädt für einen Knoten nur dessen Nachbarschaft und nicht den
/// ganzen Bestand; die Dateiliste kommt vollständig und nach Id sortiert.
/// </summary>
/// <remarks>
/// Eine echte SQLite statt einer Attrappe, weil EF Core den Vorfilter in
/// <see cref="GraphSourceProvider.LoadNeighborhoodDocumentsAsync"/> in ein SQL-<c>LIKE</c>
/// übersetzt. Eine Attrappe bestätigte nur die Erwartung an diese Übersetzung, nicht das, was
/// SQLite tatsächlich daraus macht.
/// </remarks>
public sealed class GraphSourceProviderTests : IAsyncDisposable
{
    private readonly TestIdSequence _ids = new();
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
    /// Der Kern der Abfrage: Von 40 Dokumenten dürfen nur die wenigen zurückkommen, die den
    /// Knoten überhaupt betreffen. Die Zahl steht in der Zusicherung, sonst wäre der Test
    /// auch dann grün, wenn wieder alles geladen würde.
    /// </remarks>
    [Fact]
    public async Task LoadNeighborhoodDocumentsAsync_ReturnsOnlyTheNeighborhood()
    {
        Guid middle = IdOf(1);
        Guid linking = IdOf(2);
        Guid unrelated = IdOf(3);

        await SeedDocumentAsync(middle, "mitte", ["woanders-hin"]).ConfigureAwait(true);
        await SeedDocumentAsync(linking, "zeigt-hin", ["mitte"]).ConfigureAwait(true);
        await SeedDocumentAsync(unrelated, "unbeteiligt", ["dritter-ort"]).ConfigureAwait(true);
        for (int i = 4; i <= 40; i++)
        {
            await SeedDocumentAsync(IdOf(i), "eintrag-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), []).ConfigureAwait(true);
        }

        IReadOnlyList<GraphSourceDocument> result =
            await _sut.LoadNeighborhoodDocumentsAsync(middle, "mitte", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, document => document.MarkdownFileId == middle);
        Assert.Contains(result, document => document.MarkdownFileId == linking);
        Assert.DoesNotContain(result, document => document.MarkdownFileId == unrelated);
    }

    /// <remarks>
    /// Das eigene Dokument muss auch dann dabei sein, wenn niemand auf es zeigt, sonst
    /// verlöre der Knoten seine ausgehenden Verweise.
    /// </remarks>
    [Fact]
    public async Task LoadNeighborhoodDocumentsAsync_AlwaysIncludesTheNodeItself()
    {
        Guid isolated = IdOf(1);
        await SeedDocumentAsync(isolated, "allein", ["irgendwohin"]).ConfigureAwait(true);
        await SeedDocumentAsync(IdOf(2), "anderer", ["nicht-allein"]).ConfigureAwait(true);

        IReadOnlyList<GraphSourceDocument> result =
            await _sut.LoadNeighborhoodDocumentsAsync(isolated, "allein", TestContext.Current.CancellationToken).ConfigureAwait(true);

        GraphSourceDocument single = Assert.Single(result);
        Assert.Equal(isolated, single.MarkdownFileId);
        Assert.Contains("irgendwohin", single.OutlinksJson, StringComparison.Ordinal);
    }

    /// <remarks>
    /// <para>
    /// Die unangenehme Stelle: In einem <c>LIKE</c> sind <c>%</c> und <c>_</c> Platzhalter.
    /// Käme ein Slug mit Unterstrich durch, träfe <c>a_b</c> auch <c>axb</c>; der Graph zöge
    /// eine Kante, die im Dokument nicht steht.
    /// </para>
    /// <para>
    /// Die Slug-Bildung lässt beide Zeichen nicht durch. Der Vorfilter darf trotzdem zu viel
    /// liefern, weil der Aufrufer jeden Treffer gegen die tatsächliche Verweis-Liste hält. Dieser
    /// Test prüft die andere Grenze des Musters: Die Anführungszeichen um das Ziel verhindern,
    /// dass <c>a-b</c> auch <c>a-b-und-mehr</c> trifft.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task LoadNeighborhoodDocumentsAsync_WithALongerTargetOfTheSamePrefix_DoesNotMatchIt()
    {
        Guid wanted = IdOf(1);
        Guid falseMatch = IdOf(2);

        await SeedDocumentAsync(wanted, "a-b", []).ConfigureAwait(true);
        await SeedDocumentAsync(falseMatch, "sonst", ["a-b-und-mehr"]).ConfigureAwait(true);

        IReadOnlyList<GraphSourceDocument> result =
            await _sut.LoadNeighborhoodDocumentsAsync(wanted, "a-b", TestContext.Current.CancellationToken).ConfigureAwait(true);

        // Das eigene Dokument ist immer dabei; der Nachbar mit dem längeren Ziel nicht,
        // weil die Anführungszeichen im Suchmuster das Ziel begrenzen.
        GraphSourceDocument single = Assert.Single(result);
        Assert.Equal(wanted, single.MarkdownFileId);
    }

    [Fact]
    public async Task LoadNeighborhoodDocumentsAsync_OnBlankSlug_Throws()
    {
        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.LoadNeighborhoodDocumentsAsync(IdOf(1), "   ", TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    /// <remarks>
    /// Die drei kurzen Spalten sind der ganze Zweck der Methode. Sie ersetzt das Laden des
    /// vollen Schnappschusses. Der Test hält fest, dass sie die Dateien vollständig und in
    /// stabiler Reihenfolge liefert.
    /// </remarks>
    [Fact]
    public async Task LoadFilesAsync_ReturnsEveryFileOrderedById()
    {
        await SeedDocumentAsync(IdOf(2), "zweite", []).ConfigureAwait(true);
        await SeedDocumentAsync(IdOf(1), "erste", []).ConfigureAwait(true);

        IReadOnlyList<GraphSourceFile> result = await _sut.LoadFilesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

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
            Id = _ids.Next(),
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
