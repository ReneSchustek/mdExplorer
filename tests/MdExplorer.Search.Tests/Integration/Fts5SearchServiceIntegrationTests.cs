using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Search.Tests.Integration;

/// <summary>
/// Integrationstests gegen eine echte SQLite-Datei (kein In-Memory, denn FTS5 verhält sich auf
/// In-Memory anders). Pflegt Daten über den realen <c>Fts5IndexMaintainer</c> und sucht
/// danach über den realen <c>Fts5SearchService</c>.
/// </summary>
public sealed class Fts5SearchServiceIntegrationTests
{
    [Fact]
    public async Task SearchAsync_OnSimpleWord_FindsDocument()
    {
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            Guid fileId = IdOf(1);
            await harness.SeedAsync(new SeedDocument(
                fileId,
                Title: "Notizen",
                AbsolutePath: @"C:\Wurzel\notizen.md",
                RelativePath: "notizen.md",
                SourceContentHash: "hash-1",
                FrontmatterJson: "{}",
                Body: "Dies ist ein Markdown-Dokument.",
                RawSource: "Dies ist ein Markdown-Dokument.",
                Tags: []), TestContext.Current.CancellationToken).ConfigureAwait(true);

            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            IReadOnlyList<SearchResult> results = await service
                .SearchAsync(new SearchQuery("markdown"), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            _ = Assert.Single(results);
            Assert.Equal(fileId, results[0].MarkdownFileId);
        }
    }

    [Fact]
    public async Task SearchAsync_OnPhrase_FindsOnlyPhraseMatches()
    {
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            await harness.SeedAsync(SeedFor(IdOf(2), "Alpha", "foo-bar.md", "foo bar baz"), TestContext.Current.CancellationToken).ConfigureAwait(true);
            await harness.SeedAsync(SeedFor(IdOf(3), "Beta", "bar-foo.md", "bar foo baz"), TestContext.Current.CancellationToken).ConfigureAwait(true);

            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            IReadOnlyList<SearchResult> phraseResults = await service
                .SearchAsync(new SearchQuery("\"foo bar\""), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            IReadOnlyList<SearchResult> wordResults = await service
                .SearchAsync(new SearchQuery("foo bar"), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            _ = Assert.Single(phraseResults);
            Assert.Equal("foo-bar.md", phraseResults[0].Path);
            Assert.Equal(2, wordResults.Count);
        }
    }

    [Fact]
    public async Task SearchAsync_OnDiacritics_NormalizesAndFinds()
    {
        // FTS5-Tokenizer "unicode61 remove_diacritics 2" eliminiert Diakritika (ü→u, ö→o, ä→a),
        // ersetzt jedoch nicht die deutsche ue/oe/ae-Variante. Akzeptiertes Verhalten:
        // "Munchen" findet "München", "Muenchen" hingegen nicht.
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            Guid fileId = IdOf(4);
            await harness.SeedAsync(SeedFor(fileId, "München", "muenchen.md", "Spaziergang durch München im Sommer."), TestContext.Current.CancellationToken).ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            IReadOnlyList<SearchResult> results = await service
                .SearchAsync(new SearchQuery("Munchen"), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            _ = Assert.Single(results);
            Assert.Equal(fileId, results[0].MarkdownFileId);
        }
    }

    [Fact]
    public async Task SearchAsync_OnTagFilter_RestrictsToTaggedDocuments()
    {
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            Guid taggedId = IdOf(5);
            await harness.SeedAsync(new SeedDocument(
                taggedId,
                Title: "Projekt-Notiz",
                AbsolutePath: @"C:\Wurzel\projekt.md",
                RelativePath: "projekt.md",
                SourceContentHash: "hash-tag-1",
                FrontmatterJson: "{}",
                Body: "Inhalt zum Projekt.",
                RawSource: "Inhalt zum Projekt.",
                Tags: [("projekt", "Projekt")]), TestContext.Current.CancellationToken).ConfigureAwait(true);
            await harness.SeedAsync(SeedFor(IdOf(6), "Andere", "andere.md", "Inhalt zum Projekt aber ohne Tag."), TestContext.Current.CancellationToken).ConfigureAwait(true);

            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            IReadOnlyList<SearchResult> results = await service
                .SearchAsync(new SearchQuery("tag:projekt"), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            _ = Assert.Single(results);
            Assert.Equal(taggedId, results[0].MarkdownFileId);
        }
    }

    [Fact]
    public async Task SearchAsync_OnPathFilter_RestrictsByPathPrefix()
    {
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            // Gespeichert wird der relative Pfad des Betriebssystems, wie ihn der Indexer bildet.
            await harness.SeedAsync(SeedFor(new Guid("41111111-1111-1111-1111-111111111111"), "Notiz", @"C:\Wurzel\notes\eins.md", "Inhalt mit alpha.", Path.Combine("notes", "eins.md")), TestContext.Current.CancellationToken).ConfigureAwait(true);
            await harness.SeedAsync(SeedFor(new Guid("42222222-2222-2222-2222-222222222222"), "Sonstig", @"C:\Wurzel\sonstig\zwei.md", "Inhalt mit alpha.", Path.Combine("sonstig", "zwei.md")), TestContext.Current.CancellationToken).ConfigureAwait(true);

            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            IReadOnlyList<SearchResult> results = await service
                .SearchAsync(new SearchQuery("alpha path:notes/"), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            Assert.Equal("Notiz", Assert.Single(results).Title);
        }
    }

    [Fact]
    public async Task SearchAsync_OnPathFilterWithUnderscore_MatchesTheUnderscoreLiterally()
    {
        // Im LIKE steht "_" für ein beliebiges Zeichen. Ohne Maskierung träfe "my_docs" auch
        // "myXdocs"; der Test läuft gegen echtes SQLite, weil nur dort ESCAPE wirkt.
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            await harness.SeedAsync(SeedFor(new Guid("51111111-1111-1111-1111-111111111111"), "Gemeint", @"C:\Wurzel\my_docs\a.md", "Inhalt mit alpha.", Path.Combine("my_docs", "a.md")), TestContext.Current.CancellationToken).ConfigureAwait(true);
            await harness.SeedAsync(SeedFor(new Guid("52222222-2222-2222-2222-222222222222"), "Fremd", @"C:\Wurzel\myXdocs\b.md", "Inhalt mit alpha.", Path.Combine("myXdocs", "b.md")), TestContext.Current.CancellationToken).ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            IReadOnlyList<SearchResult> results = await service
                .SearchAsync(new SearchQuery("alpha path:my_docs"), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            Assert.Equal("Gemeint", Assert.Single(results).Title);
        }
    }

    [Fact]
    public async Task SearchAsync_OnSqlInjectionAttempt_RemainsSafeAndReturnsEmpty()
    {
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            await harness.SeedAsync(SeedFor(IdOf(7), "Doku", "doku.md", "Eine harmlose Notiz."), TestContext.Current.CancellationToken).ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            IReadOnlyList<SearchResult> results = await service
                .SearchAsync(new SearchQuery("'; DROP TABLE MarkdownSearchIndex --"), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            Assert.Empty(results);

            IReadOnlyList<SearchResult> sanityCheck = await service
                .SearchAsync(new SearchQuery("notiz"), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            _ = Assert.Single(sanityCheck);
        }
    }

    [Fact]
    public async Task SearchAsync_OnANoteContainingMarkupText_KeepsItAsTextAndMarksOnlyTheHit()
    {
        // Eine Notiz über HTML enthält <mark> wörtlich. Als Trennzeichen der Trefferstellen hätte
        // es die Hervorhebung verschoben; mit eigenen Zeichen bleibt es gewöhnlicher Text.
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            await harness.SeedAsync(SeedFor(IdOf(9), "Html", "html.md", "Das Element <mark> hebt ein wort hervor."), TestContext.Current.CancellationToken).ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            IReadOnlyList<SearchResult> results = await harness.CreateSearchService(scope)
                .SearchAsync(new SearchQuery("wort"), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            string snippet = Assert.Single(results).Snippet;
            Assert.Contains("<mark>", snippet, StringComparison.Ordinal);
            Assert.Contains(SnippetMarkers.Open + "wort" + SnippetMarkers.Close, snippet, StringComparison.Ordinal);
            Assert.Equal(1, snippet.Split(SnippetMarkers.Open).Length - 1);
        }
    }

    [Fact]
    public async Task SearchAsync_OnSnippetMarker_ReturnsHighlightPositions()
    {
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            Guid fileId = IdOf(8);
            await harness.SeedAsync(SeedFor(fileId, "Highlight", "highlight.md", "Treffer auf wort in einem Absatz."), TestContext.Current.CancellationToken).ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            IReadOnlyList<SearchResult> results = await service
                .SearchAsync(new SearchQuery("wort"), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            _ = Assert.Single(results);
            Assert.Contains(SnippetMarkers.Open + "wort" + SnippetMarkers.Close, results[0].Snippet, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Maintainer_OnDocumentDeletion_RemovesTheHit()
    {
        // Der Abgleich räumt auf, kein Auslöser je Zeile; die
        // Begründung steht in TriggerDiagnosticsTests. Für die Suche zählt das Ergebnis:
        // Nach dem nächsten Abgleich ist der Treffer weg.
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            Guid fileId = IdOf(9);
            await harness.SeedAsync(SeedFor(fileId, "Delete-Me", "delete.md", "Inhalt zum löschen."), TestContext.Current.CancellationToken).ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using (IServiceScope before = harness.Services.CreateScope())
            {
                IReadOnlyList<SearchResult> before1 = await harness.CreateSearchService(before)
                    .SearchAsync(new SearchQuery("löschen"), TestContext.Current.CancellationToken)
                    .ConfigureAwait(true);
                _ = Assert.Single(before1);
            }

            _ = await harness.DeleteDocumentAsync(fileId, TestContext.Current.CancellationToken).ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using IServiceScope after = harness.Services.CreateScope();
            IReadOnlyList<SearchResult> afterResults = await harness.CreateSearchService(after)
                .SearchAsync(new SearchQuery("löschen"), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            Assert.Empty(afterResults);
        }
    }

    [Fact]
    public async Task Maintainer_OnUpdatedSource_KeepsIndexConsistent()
    {
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            Guid fileId = IdOf(10);
            await harness.SeedAsync(SeedFor(fileId, "Update", "update.md", "Erste Fassung mit alpha."), TestContext.Current.CancellationToken).ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using (IServiceScope scopeOne = harness.Services.CreateScope())
            {
                IReadOnlyList<SearchResult> firstHits = await harness.CreateSearchService(scopeOne)
                    .SearchAsync(new SearchQuery("alpha"), TestContext.Current.CancellationToken)
                    .ConfigureAwait(true);
                _ = Assert.Single(firstHits);
            }

            using (IServiceScope updateScope = harness.Services.CreateScope())
            {
                Data.MdExplorerDbContext dbContext = updateScope.ServiceProvider.GetRequiredService<Data.MdExplorerDbContext>();
                Core.Models.MarkdownDocument doc = dbContext.Set<Core.Models.MarkdownDocument>()
                    .Single(d => d.MarkdownFileId == fileId);
                doc.SourceContentHash = "hash-updated";
                doc.FrontmatterJson = "{}";
                _ = await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            }

            harness.FileSystem.AddFile(@"C:\Wurzel\update.md", "Zweite Fassung mit beta.");
            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using IServiceScope afterUpdate = harness.Services.CreateScope();
            IReadOnlyList<SearchResult> alphaResults = await harness.CreateSearchService(afterUpdate)
                .SearchAsync(new SearchQuery("alpha"), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            IReadOnlyList<SearchResult> betaResults = await harness.CreateSearchService(afterUpdate)
                .SearchAsync(new SearchQuery("beta"), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            Assert.Empty(alphaResults);
            _ = Assert.Single(betaResults);
        }
    }

    [Fact]
    public async Task SearchAsync_OnTwoHundredDocuments_FindsEveryOneOfThem()
    {
        // Die Laufzeit misst SearchBenchmarks im Messprojekt; eine Stoppuhr im Testlauf hinge an
        // der Auslastung. Hier zählt, dass ein größerer Bestand vollständig gefunden wird.
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            const int DocumentCount = 200;
            for (int i = 0; i < DocumentCount; i++)
            {
                await harness.SeedAsync(SeedFor(
                    IdOf(1000 + i),
                    $"Dokument-{i}",
                    $@"C:\Wurzel\doc-{i}.md",
                    $"Inhalt {i} mit gemeinsamem wort und einem eigenen begriff-{i}."),
                    TestContext.Current.CancellationToken).ConfigureAwait(true);
            }

            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            // Eine Seite fasst höchstens 100 Treffer; die zweite Seite muss den Rest bringen.
            IReadOnlyList<SearchResult> first = await service
                .SearchAsync(new SearchQuery("wort", Take: 100), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            IReadOnlyList<SearchResult> second = await service
                .SearchAsync(new SearchQuery("wort", Take: 100, Skip: 100), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            Assert.Equal(DocumentCount, first.Concat(second).Select(result => result.MarkdownFileId).Distinct().Count());
        }
    }

    private static SeedDocument SeedFor(Guid fileId, string title, string fileName, string body) =>
        SeedFor(fileId, title, $@"C:\Wurzel\{fileName}", body, fileName);

    private static SeedDocument SeedFor(Guid fileId, string title, string absolutePath, string body, string relativePath) =>
        new(
            fileId,
            Title: title,
            AbsolutePath: absolutePath,
            RelativePath: relativePath,
            SourceContentHash: "hash-" + fileId.ToString("N"),
            FrontmatterJson: "{}",
            Body: body,
            RawSource: body,
            Tags: []);

    private static Guid IdOf(int index) =>
        new(index.ToString("D8", System.Globalization.CultureInfo.InvariantCulture) + "-0000-0000-0000-000000000000");
}
