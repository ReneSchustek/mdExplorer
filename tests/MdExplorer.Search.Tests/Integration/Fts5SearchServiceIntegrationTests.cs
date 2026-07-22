using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Search.Tests.Integration;

/// <summary>
/// Integrationstests gegen eine echte SQLite-Datei (kein In-Memory — FTS5 verhält sich auf
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
            Guid fileId = Guid.NewGuid();
            await harness.SeedAsync(new SeedDocument(
                fileId,
                Title: "Notizen",
                AbsolutePath: @"C:\Wurzel\notizen.md",
                RelativePath: "notizen.md",
                SourceContentHash: "hash-1",
                FrontmatterJson: "{}",
                Body: "Dies ist ein Markdown-Dokument.",
                RawSource: "Dies ist ein Markdown-Dokument.",
                Tags: []), CancellationToken.None).ConfigureAwait(true);

            _ = await harness.Maintainer.SynchronizeAsync(CancellationToken.None).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            IReadOnlyList<SearchResult> results = await service
                .SearchAsync(new SearchQuery("markdown"), CancellationToken.None)
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
            await harness.SeedAsync(SeedFor(Guid.NewGuid(), "Alpha", "foo-bar.md", "foo bar baz"), CancellationToken.None).ConfigureAwait(true);
            await harness.SeedAsync(SeedFor(Guid.NewGuid(), "Beta", "bar-foo.md", "bar foo baz"), CancellationToken.None).ConfigureAwait(true);

            _ = await harness.Maintainer.SynchronizeAsync(CancellationToken.None).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            IReadOnlyList<SearchResult> phraseResults = await service
                .SearchAsync(new SearchQuery("\"foo bar\""), CancellationToken.None)
                .ConfigureAwait(true);
            IReadOnlyList<SearchResult> wordResults = await service
                .SearchAsync(new SearchQuery("foo bar"), CancellationToken.None)
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
            Guid fileId = Guid.NewGuid();
            await harness.SeedAsync(SeedFor(fileId, "München", "muenchen.md", "Spaziergang durch München im Sommer."), CancellationToken.None).ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(CancellationToken.None).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            IReadOnlyList<SearchResult> results = await service
                .SearchAsync(new SearchQuery("Munchen"), CancellationToken.None)
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
            Guid taggedId = Guid.NewGuid();
            await harness.SeedAsync(new SeedDocument(
                taggedId,
                Title: "Projekt-Notiz",
                AbsolutePath: @"C:\Wurzel\projekt.md",
                RelativePath: "projekt.md",
                SourceContentHash: "hash-tag-1",
                FrontmatterJson: "{}",
                Body: "Inhalt zum Projekt.",
                RawSource: "Inhalt zum Projekt.",
                Tags: [("projekt", "Projekt")]), CancellationToken.None).ConfigureAwait(true);
            await harness.SeedAsync(SeedFor(Guid.NewGuid(), "Andere", "andere.md", "Inhalt zum Projekt aber ohne Tag."), CancellationToken.None).ConfigureAwait(true);

            _ = await harness.Maintainer.SynchronizeAsync(CancellationToken.None).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            IReadOnlyList<SearchResult> results = await service
                .SearchAsync(new SearchQuery("tag:projekt"), CancellationToken.None)
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
            await harness.SeedAsync(SeedFor(Guid.NewGuid(), "Notiz", @"C:\Wurzel\notes\eins.md", "Inhalt mit alpha.", "notes/eins.md"), CancellationToken.None).ConfigureAwait(true);
            await harness.SeedAsync(SeedFor(Guid.NewGuid(), "Sonstig", @"C:\Wurzel\sonstig\zwei.md", "Inhalt mit alpha.", "sonstig/zwei.md"), CancellationToken.None).ConfigureAwait(true);

            _ = await harness.Maintainer.SynchronizeAsync(CancellationToken.None).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            IReadOnlyList<SearchResult> results = await service
                .SearchAsync(new SearchQuery("alpha path:notes/"), CancellationToken.None)
                .ConfigureAwait(true);

            _ = Assert.Single(results);
            Assert.StartsWith("notes/", results[0].Path, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SearchAsync_OnSqlInjectionAttempt_RemainsSafeAndReturnsEmpty()
    {
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            await harness.SeedAsync(SeedFor(Guid.NewGuid(), "Doku", "doku.md", "Eine harmlose Notiz."), CancellationToken.None).ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(CancellationToken.None).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            IReadOnlyList<SearchResult> results = await service
                .SearchAsync(new SearchQuery("'; DROP TABLE MarkdownSearchIndex --"), CancellationToken.None)
                .ConfigureAwait(true);

            Assert.Empty(results);

            IReadOnlyList<SearchResult> sanityCheck = await service
                .SearchAsync(new SearchQuery("notiz"), CancellationToken.None)
                .ConfigureAwait(true);
            _ = Assert.Single(sanityCheck);
        }
    }

    [Fact]
    public async Task SearchAsync_OnSnippetMarker_ReturnsHighlightPositions()
    {
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            Guid fileId = Guid.NewGuid();
            await harness.SeedAsync(SeedFor(fileId, "Highlight", "highlight.md", "Treffer auf wort in einem Absatz."), CancellationToken.None).ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(CancellationToken.None).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            IReadOnlyList<SearchResult> results = await service
                .SearchAsync(new SearchQuery("wort"), CancellationToken.None)
                .ConfigureAwait(true);

            _ = Assert.Single(results);
            Assert.Contains("<mark>", results[0].Snippet, StringComparison.Ordinal);
            Assert.NotEmpty(results[0].Highlights);
        }
    }

    [Fact]
    public async Task Maintainer_OnDocumentDeletion_ReflectsDeletionViaTrigger()
    {
        // Der <c>AFTER DELETE</c>-Trigger auf <c>MarkdownDocuments</c> entfernt die FTS5-Zeile sofort,
        // ohne dass der Maintainer-Resync nötig ist (siehe <c>TriggerDiagnosticsTests</c>).
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            Guid fileId = Guid.NewGuid();
            await harness.SeedAsync(SeedFor(fileId, "Delete-Me", "delete.md", "Inhalt zum löschen."), CancellationToken.None).ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(CancellationToken.None).ConfigureAwait(true);

            using (IServiceScope before = harness.Services.CreateScope())
            {
                IReadOnlyList<SearchResult> before1 = await harness.CreateSearchService(before)
                    .SearchAsync(new SearchQuery("löschen"), CancellationToken.None)
                    .ConfigureAwait(true);
                _ = Assert.Single(before1);
            }

            _ = await harness.DeleteDocumentAsync(fileId, CancellationToken.None).ConfigureAwait(true);

            using IServiceScope after = harness.Services.CreateScope();
            IReadOnlyList<SearchResult> afterResults = await harness.CreateSearchService(after)
                .SearchAsync(new SearchQuery("löschen"), CancellationToken.None)
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
            Guid fileId = Guid.NewGuid();
            await harness.SeedAsync(SeedFor(fileId, "Update", "update.md", "Erste Fassung mit alpha."), CancellationToken.None).ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(CancellationToken.None).ConfigureAwait(true);

            using (IServiceScope scopeOne = harness.Services.CreateScope())
            {
                IReadOnlyList<SearchResult> firstHits = await harness.CreateSearchService(scopeOne)
                    .SearchAsync(new SearchQuery("alpha"), CancellationToken.None)
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
                _ = await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(true);
            }

            harness.FileSystem.AddFile(@"C:\Wurzel\update.md", "Zweite Fassung mit beta.");
            _ = await harness.Maintainer.SynchronizeAsync(CancellationToken.None).ConfigureAwait(true);

            using IServiceScope afterUpdate = harness.Services.CreateScope();
            IReadOnlyList<SearchResult> alphaResults = await harness.CreateSearchService(afterUpdate)
                .SearchAsync(new SearchQuery("alpha"), CancellationToken.None)
                .ConfigureAwait(true);
            IReadOnlyList<SearchResult> betaResults = await harness.CreateSearchService(afterUpdate)
                .SearchAsync(new SearchQuery("beta"), CancellationToken.None)
                .ConfigureAwait(true);

            Assert.Empty(alphaResults);
            _ = Assert.Single(betaResults);
        }
    }

    [Fact]
    public async Task SearchAsync_OnLargeDataset_RespondsBelow100MilliSeconds()
    {
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            const int documentCount = 200;
            for (int i = 0; i < documentCount; i++)
            {
                await harness.SeedAsync(SeedFor(
                    Guid.NewGuid(),
                    $"Dokument-{i}",
                    $@"C:\Wurzel\doc-{i}.md",
                    $"Inhalt {i} mit gemeinsamem wort und einem eigenen begriff-{i}."),
                    CancellationToken.None).ConfigureAwait(true);
            }

            _ = await harness.Maintainer.SynchronizeAsync(CancellationToken.None).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);

            // Warmup
            _ = await service
                .SearchAsync(new SearchQuery("wort"), CancellationToken.None)
                .ConfigureAwait(true);

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            IReadOnlyList<SearchResult> results = await service
                .SearchAsync(new SearchQuery("wort"), CancellationToken.None)
                .ConfigureAwait(true);
            stopwatch.Stop();

            Assert.NotEmpty(results);
            Assert.True(
                stopwatch.ElapsedMilliseconds < 100,
                $"FTS5-Suche dauerte {stopwatch.ElapsedMilliseconds} ms — Performance-Budget 100 ms verletzt.");
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
            SourceContentHash: Guid.NewGuid().ToString("N"),
            FrontmatterJson: "{}",
            Body: body,
            RawSource: body,
            Tags: []);
}
