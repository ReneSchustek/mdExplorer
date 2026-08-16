using System.Globalization;
using MdExplorer.Core.Abstractions;
using MdExplorer.Graph.Models;
using MdExplorer.Graph.Options;
using MdExplorer.Graph.Services;
using MdExplorer.Graph.Tests.Fakes;
using MdExplorer.Parser.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Graph.Tests.Services;

/// <summary>
/// Hält fest, dass die Verbindungen eines Dokuments nur die Nachbarschaft laden — und
/// trotzdem dasselbe liefern wie der Weg über den ganzen Bestand.
/// </summary>
/// <remarks>
/// Der Umbau vom 16.08.2026 tauscht Umfang gegen Umfang, nicht Logik gegen Logik: Die
/// Auflösung von Name auf Datei bleibt dieselbe Funktion. Genau das muss belegt sein, sonst
/// tauscht man ein Leistungsproblem gegen zwei Auflösungen, die auseinanderlaufen.
/// </remarks>
public sealed class GraphServiceNeighborhoodTests
{
    [Fact]
    public async Task RelationsMatchWhatTheFullSnapshotWouldSay()
    {
        GraphSourceData data = Corpus(120);

        foreach (GraphSourceFile file in data.Files)
        {
            DocumentRelations viaNeighborhood = await Build(data)
                .GetRelationsAsync(file.Id, TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            DocumentRelations viaFullSnapshot = FromFullSnapshot(data, file.Id);

            Assert.Equal(
                viaFullSnapshot.Outgoing.Select(entry => entry.MarkdownFileId),
                viaNeighborhood.Outgoing.Select(entry => entry.MarkdownFileId));
            Assert.Equal(
                viaFullSnapshot.Incoming.Select(entry => entry.MarkdownFileId),
                viaNeighborhood.Incoming.Select(entry => entry.MarkdownFileId));
        }
    }

    /// <remarks>
    /// Die eigentliche Zusage des Umbaus. Ohne diese Prüfung wäre der Aufruf still wieder
    /// beim vollen Bestand gelandet, sobald jemand die Bequemlichkeit sucht.
    /// </remarks>
    [Fact]
    public async Task RelationsNeverAskForTheFullSnapshot()
    {
        GraphSourceData data = Corpus(120);
        FakeGraphSourceProvider provider = new(data);
        GraphService sut = Build(provider);

        _ = await sut.GetRelationsAsync(data.Files[7].Id, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, provider.FullLoadCount);
        Assert.Equal(1, provider.NeighborhoodLoadCount);
    }

    /// <remarks>
    /// Aus 120 Dokumenten werden eine Handvoll. Die Zahl ist die Aussage — ohne sie wäre
    /// „nur die Nachbarschaft" eine Behauptung.
    /// </remarks>
    [Fact]
    public async Task OnlyAHandfulOfLinkListsAreRead()
    {
        GraphSourceData data = Corpus(120);
        FakeGraphSourceProvider provider = new(data);
        GraphService sut = Build(provider);

        _ = await sut.GetRelationsAsync(data.Files[7].Id, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.InRange(provider.LastNeighborhoodSize, 1, 10);
        Assert.True(
            provider.LastNeighborhoodSize < data.Documents.Count / 4,
            $"Erwartet wurde ein Bruchteil von {data.Documents.Count}, geliefert wurden {provider.LastNeighborhoodSize}.");
    }

    /// <remarks>
    /// Der Vorfilter ist eine Textsuche und liefert zu viel: Ein Dokument, das
    /// <c>„notiz-7-anhang"</c> nennt, enthält die Zeichenfolge <c>„notiz-7"</c> nicht — wohl
    /// aber eines, das <c>„notiz-70"</c>… deshalb sucht der Filter mit Anführungszeichen.
    /// Was trotzdem durchrutscht, muss beim Auswerten verschwinden.
    /// </remarks>
    [Fact]
    public async Task APrefilterHitThatIsNoLinkDoesNotBecomeAnEdge()
    {
        Guid ziel = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid fremd = Guid.Parse("22222222-2222-2222-2222-222222222222");
        GraphSourceData data = new(
            [
                new GraphSourceFile(ziel, "Notiz", "Notiz.md"),
                new GraphSourceFile(fremd, "Fremd", "Fremd.md"),
            ],
            [
                new GraphSourceDocument(ziel, """[]"""),
                // Der Slug steht im Text der Liste, ist aber kein Eintrag des Arrays.
                new GraphSourceDocument(fremd, """["etwas-anderes"]"""),
            ]);

        DocumentRelations relations = await Build(data)
            .GetRelationsAsync(ziel, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Empty(relations.Incoming);
    }

    [Fact]
    public async Task AnUnknownDocumentYieldsNothing()
    {
        GraphSourceData data = Corpus(5);

        DocumentRelations relations = await Build(data)
            .GetRelationsAsync(Guid.NewGuid(), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Empty(relations.Outgoing);
        Assert.Empty(relations.Incoming);
    }

    /// <summary>Ein Bestand, in dem jede Notiz auf ihre beiden Nachbarinnen zeigt.</summary>
    private static GraphSourceData Corpus(int count)
    {
        List<GraphSourceFile> files = new(count);
        List<GraphSourceDocument> documents = new(count);

        for (int index = 0; index < count; index++)
        {
            Guid id = IdOf(index);
            string name = "Notiz-" + index.ToString(CultureInfo.InvariantCulture);
            files.Add(new GraphSourceFile(id, name, name + ".md"));

            string previous = "notiz-" + Math.Max(0, index - 1).ToString(CultureInfo.InvariantCulture);
            string next = "notiz-" + Math.Min(count - 1, index + 1).ToString(CultureInfo.InvariantCulture);
            documents.Add(new GraphSourceDocument(id, $"""["{previous}","{next}"]"""));
        }

        return new GraphSourceData(files, documents);
    }

    /// <remarks>
    /// Um eins versetzt: Index 0 ergäbe sonst <see cref="Guid.Empty"/>, und die ist der
    /// vereinbarte Wert für „kein Dokument".
    /// </remarks>
    private static Guid IdOf(int index) =>
        Guid.Parse((index + 1).ToString("D8", CultureInfo.InvariantCulture) + "-0000-0000-0000-000000000000");

    /// <summary>
    /// Was der Weg über den ganzen Bestand liefern würde — als unabhängige Rechnung.
    /// </summary>
    private static DocumentRelations FromFullSnapshot(GraphSourceData data, Guid markdownFileId)
    {
        TagNormalizer normalizer = new();
        Dictionary<string, Guid> slugIndex = new(StringComparer.Ordinal);
        foreach (GraphSourceFile file in data.Files)
        {
            if (normalizer.TryToSlug(file.FileNameWithoutExtension, out string slug))
            {
                _ = slugIndex.TryAdd(slug, file.Id);
            }
        }

        Dictionary<Guid, GraphSourceFile> filesById = data.Files.ToDictionary(file => file.Id);
        List<(Guid Source, Guid Target)> edges = [];
        foreach (GraphSourceDocument document in data.Documents)
        {
            foreach (string raw in System.Text.Json.JsonSerializer.Deserialize<string[]>(document.OutlinksJson) ?? [])
            {
                if (normalizer.TryToSlug(raw, out string slug)
                    && slugIndex.TryGetValue(slug, out Guid target)
                    && target != document.MarkdownFileId)
                {
                    edges.Add((document.MarkdownFileId, target));
                }
            }
        }

        return new DocumentRelations(
            Related(edges.Where(edge => edge.Source == markdownFileId).Select(edge => edge.Target), filesById),
            Related(edges.Where(edge => edge.Target == markdownFileId).Select(edge => edge.Source), filesById));
    }

    private static IReadOnlyList<RelatedDocument> Related(
        IEnumerable<Guid> ids,
        Dictionary<Guid, GraphSourceFile> filesById) =>
        [
            .. ids.Distinct()
                .Where(filesById.ContainsKey)
                .Select(id => filesById[id])
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(file => new RelatedDocument(file.Id, file.FileNameWithoutExtension, file.RelativePath))
        ];

    private static GraphService Build(GraphSourceData data) => Build(new FakeGraphSourceProvider(data));

    private static GraphService Build(IGraphSourceProvider provider) =>
        new(
            provider,
            new TagNormalizer(),
            Microsoft.Extensions.Options.Options.Create(new GraphOptions { IncludeIsolatedNodes = true }),
            NullLogger<GraphService>.Instance);
}
