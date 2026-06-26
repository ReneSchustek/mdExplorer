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

public sealed class ParseOrchestratorTests
{
    [Fact]
    public async Task RunOnce_OnMissingDocument_PersistsNewEntry()
    {
        TestHarness harness = new();
        Guid fileId = harness.AddSource("/r/foo.md", "abchash", "# Titel\n\nBody mit #foo.");

        await harness.Sut.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, harness.DocRepo.SaveCallCount);
        Assert.True(harness.DocRepo.Snapshot.ContainsKey(fileId));
        Assert.Equal("abchash", harness.DocRepo.Snapshot[fileId].SourceContentHash);
    }

    [Fact]
    public async Task RunOnce_OnUnchangedHash_DoesNotReParse()
    {
        TestHarness harness = new();
        _ = harness.AddSource("/r/foo.md", "h1", "Body #foo");

        await harness.Sut.RunOnceAsync(CancellationToken.None);
        int writesAfterFirst = harness.DocRepo.ParseCount;

        await harness.Sut.RunOnceAsync(CancellationToken.None);

        Assert.Equal(writesAfterFirst, harness.DocRepo.ParseCount);
    }

    [Fact]
    public async Task RunOnce_OnChangedHash_UpdatesExistingDocument()
    {
        TestHarness harness = new();
        Guid fileId = harness.AddSource("/r/foo.md", "h1", "Body #foo");
        await harness.Sut.RunOnceAsync(CancellationToken.None);

        harness.UpdateSource(fileId, "h2", "Body #bar");
        await harness.Sut.RunOnceAsync(CancellationToken.None);

        Assert.Equal("h2", harness.DocRepo.Snapshot[fileId].SourceContentHash);
        Assert.True(harness.TagRepo.TagsBySlug.ContainsKey("bar"));
        _ = Assert.Single(harness.TagRepo.FileLinks[fileId]);
    }

    [Fact]
    public async Task RunOnce_OnReadFailure_SkipsFileWithoutCrash()
    {
        TestHarness harness = new();
        Guid bad = harness.AddSource("/r/bad.md", "h1", "ignored");
        Guid good = harness.AddSource("/r/good.md", "h1", "Body #good");
        _ = harness.FileSystem.FailOnRead.Add("/r/bad.md".Replace('/', Path.DirectorySeparatorChar));

        await harness.Sut.RunOnceAsync(CancellationToken.None);

        Assert.False(harness.DocRepo.Snapshot.ContainsKey(bad));
        Assert.True(harness.DocRepo.Snapshot.ContainsKey(good));
    }

    [Fact]
    public async Task RunOnce_OnFrontmatterTagsAndBodyTags_CreatesAllSlugs()
    {
        TestHarness harness = new();
        Guid fileId = harness.AddSource("/r/f.md", "h1", """
            ---
            tags: [Alpha, Beta]
            ---
            Body mit #gamma.
            """);

        await harness.Sut.RunOnceAsync(CancellationToken.None);

        Assert.Contains("alpha", harness.TagRepo.TagsBySlug.Keys, StringComparer.Ordinal);
        Assert.Contains("beta", harness.TagRepo.TagsBySlug.Keys, StringComparer.Ordinal);
        Assert.Contains("gamma", harness.TagRepo.TagsBySlug.Keys, StringComparer.Ordinal);
        Assert.Equal(3, harness.TagRepo.FileLinks[fileId].Count);
    }

    [Fact]
    public async Task RunOnce_ReusesExistingTags_DoesNotDuplicate()
    {
        TestHarness harness = new();
        _ = harness.AddSource("/r/a.md", "h1", "#shared");
        _ = harness.AddSource("/r/b.md", "h1", "#shared");

        await harness.Sut.RunOnceAsync(CancellationToken.None);

        _ = Assert.Single(harness.TagRepo.TagsBySlug);
        Assert.True(harness.TagRepo.TagsBySlug.ContainsKey("shared"));
    }

    // Reproduktion des Markdig-Depth-Limit-Crashs (User-Report 2026-06-12).
    // Wir testen die Resilienz des Orchestrators gegen ein einzelnes File, das beim Parser
    // ArgumentException wirft (z. B. depth-limit-exceeded aus Markdig). Ohne Catch
    // killt das den Parser-Service nach dem ersten schlechten File.
    [Fact]
    public async Task RunOnce_OnParserThrowingArgumentException_SkipsFileAndContinuesWithRest()
    {
        const string BadContent = "DEEPLY_NESTED_PAYLOAD_FOR_TEST";
        ThrowingParserHarness harness = new(
            failingContent: BadContent,
            failure: new ArgumentException("Markdown elements in the input are too deeply nested - depth limit exceeded."));
        Guid badId = harness.AddSource("/r/bad.md", "h1", BadContent);
        Guid goodId = harness.AddSource("/r/good.md", "h1", "Body #ok.");

        await harness.Sut.RunOnceAsync(CancellationToken.None);

        Assert.False(harness.DocRepo.Snapshot.ContainsKey(badId));
        Assert.True(harness.DocRepo.Snapshot.ContainsKey(goodId));
        Assert.True(harness.TagRepo.TagsBySlug.ContainsKey("ok"));
    }

    [Fact]
    public async Task RunOnce_OnParserThrowingInvalidOperationException_SkipsFileAndContinuesWithRest()
    {
        const string BadContent = "INVALID_FRONTMATTER_PAYLOAD";
        ThrowingParserHarness harness = new(
            failingContent: BadContent,
            failure: new InvalidOperationException("Yaml-Frontmatter ist kaputt."));
        Guid badId = harness.AddSource("/r/bad.md", "h1", BadContent);
        Guid goodId = harness.AddSource("/r/good.md", "h1", "Body #ok2.");

        await harness.Sut.RunOnceAsync(CancellationToken.None);

        Assert.False(harness.DocRepo.Snapshot.ContainsKey(badId));
        Assert.True(harness.DocRepo.Snapshot.ContainsKey(goodId));
        Assert.True(harness.TagRepo.TagsBySlug.ContainsKey("ok2"));
    }

    // Defense-in-Depth — wenn eine ArgumentException ueber den ParseOneAsync-
    // Catch hinaus durchreicht (z. B. aus einer Sub-Pipeline oder einem anderen Throwing-
    // Service), darf TryRunOnceAsync sie schlucken und der Periodic-Loop weiterlaufen.
    [Fact]
    public async Task TryRunOnce_OnUnexpectedArgumentException_LogsAndContinues()
    {
        TestHarness harness = new();
        harness.Source.ThrowOnNextEnumeration = new ArgumentException("simulated depth-limit-bubble");

        // Darf NICHT werfen — TryRunOnceAsync fasert das in den Defense-in-Depth-Catches ab.
        await harness.Sut.TryRunOnceAsync(CancellationToken.None);

        // Zweiter Tick laeuft normal weiter.
        _ = harness.AddSource("/r/good.md", "h1", "#weiter");
        await harness.Sut.TryRunOnceAsync(CancellationToken.None);

        Assert.True(harness.TagRepo.TagsBySlug.ContainsKey("weiter"));
    }

    [Fact]
    public async Task TryRunOnce_OnUnexpectedInvalidOperationException_LogsAndContinues()
    {
        TestHarness harness = new();
        harness.Source.ThrowOnNextEnumeration = new InvalidOperationException("simulated parser state");

        await harness.Sut.TryRunOnceAsync(CancellationToken.None);

        _ = harness.AddSource("/r/good.md", "h1", "#weiter");
        await harness.Sut.TryRunOnceAsync(CancellationToken.None);

        Assert.True(harness.TagRepo.TagsBySlug.ContainsKey("weiter"));
    }

    // Reproduktions-Test fuer das UNIQUE-constraint-Fehlerbild aus dem User-Report.
    // Ohne Batch-Cache wuerde tagRepo.AddAsync(Tag{Slug="shared"}) zweimal aufgerufen (pro Datei)
    // und SaveChanges einen UNIQUE constraint failed: Tags.Slug werfen.
    [Fact]
    public async Task ProcessBatch_OnTwoFilesSharingNewTag_DoesNotThrowUniqueConstraint()
    {
        TestHarness harness = new();
        Guid a = harness.AddSource("/r/a.md", "h1", "Body mit #shared.");
        Guid b = harness.AddSource("/r/b.md", "h1", "Anderes Body mit #shared!");

        await harness.Sut.RunOnceAsync(CancellationToken.None);

        _ = Assert.Single(harness.TagRepo.TagsBySlug);
        Assert.True(harness.TagRepo.TagsBySlug.ContainsKey("shared"));
        Guid linkA = Assert.Single(harness.TagRepo.FileLinks[a]);
        Guid linkB = Assert.Single(harness.TagRepo.FileLinks[b]);
        Assert.Equal(linkA, linkB);
    }

    [Fact]
    public async Task RunOnce_StoresGzippedHtmlBlob()
    {
        TestHarness harness = new();
        Guid fileId = harness.AddSource("/r/f.md", "h1", "# Titel\n\nText.");

        await harness.Sut.RunOnceAsync(CancellationToken.None);

        ReadOnlyMemory<byte> blob = harness.DocRepo.Snapshot[fileId].RenderedHtmlGz;
        string html = GzipHelper.Decompress(blob);
        Assert.Contains("<h1", html, StringComparison.Ordinal);
    }

    private sealed class TestHarness
    {
        public FakeFileSystem FileSystem { get; } = new();
        public FakeMarkdownSourceProvider Source { get; } = new();
        public FakeMarkdownDocumentRepository DocRepo { get; } = new();
        public FakeTagRepository TagRepo { get; } = new();
        public ParseOrchestrator Sut { get; }

        public TestHarness()
        {
            DocRepo.OnSaveChangesAsync = ct => TagRepo.SaveChangesAsync(ct);

            ServiceCollection services = new();
            _ = services.AddSingleton<IMarkdownSourceProvider>(Source);
            _ = services.AddSingleton<IMarkdownDocumentRepository>(DocRepo);
            _ = services.AddSingleton<ITagRepository>(TagRepo);
            ServiceProvider provider = services.BuildServiceProvider();

            TagNormalizer normalizer = new();
            MarkdigParser parser = new(
                new FrontmatterExtractor(),
                new TagExtractor(new FakeSettingsService()),
                new WikiLinkExtractor(),
                normalizer);
            ParserOptions parserOptions = new() { MaxParallelism = 2, BatchSize = 100, PollIntervalSeconds = 1 };
            FakeTimeProvider timeProvider = new();

            Sut = new ParseOrchestrator(
                provider.GetRequiredService<IServiceScopeFactory>(),
                FileSystem,
                parser,
                MEOptions.Create(parserOptions),
                timeProvider,
                NullLogger<ParseOrchestrator>.Instance);
        }

        public Guid AddSource(string path, string contentHash, string content)
        {
            Guid id = Guid.NewGuid();
            string normalized = path.Replace('/', Path.DirectorySeparatorChar);
            FileSystem.AddFile(normalized, content);
            Source.Sources.Add(new MarkdownSourceSnapshot(id, normalized, contentHash));
            return id;
        }

        public void UpdateSource(Guid fileId, string newHash, string newContent)
        {
            for (int i = 0; i < Source.Sources.Count; i++)
            {
                MarkdownSourceSnapshot existing = Source.Sources[i];
                if (existing.Id == fileId)
                {
                    FileSystem.AddFile(existing.AbsolutePath, newContent);
                    Source.Sources[i] = existing with { ContentHash = newHash };
                    return;
                }
            }
            throw new InvalidOperationException($"Source with id {fileId} not found.");
        }
    }

    // Harness mit einem Parser, der bei einem bestimmten Roh-Inhalt wirft. Damit testen
    // wir die ParseOneAsync-Catch-Pfade unabhaengig vom Markdig-Versionsverhalten.
    private sealed class ThrowingParserHarness
    {
        public FakeFileSystem FileSystem { get; } = new();
        public FakeMarkdownSourceProvider Source { get; } = new();
        public FakeMarkdownDocumentRepository DocRepo { get; } = new();
        public FakeTagRepository TagRepo { get; } = new();
        public ParseOrchestrator Sut { get; }

        public ThrowingParserHarness(string failingContent, Exception failure)
        {
            DocRepo.OnSaveChangesAsync = ct => TagRepo.SaveChangesAsync(ct);

            ServiceCollection services = new();
            _ = services.AddSingleton<IMarkdownSourceProvider>(Source);
            _ = services.AddSingleton<IMarkdownDocumentRepository>(DocRepo);
            _ = services.AddSingleton<ITagRepository>(TagRepo);
            ServiceProvider provider = services.BuildServiceProvider();

            TagNormalizer normalizer = new();
            MarkdigParser baseParser = new(
                new FrontmatterExtractor(),
                new TagExtractor(new FakeSettingsService()),
                new WikiLinkExtractor(),
                normalizer);
            ContentBasedThrowingParser throwingParser = new(baseParser, failingContent, failure);
            ParserOptions parserOptions = new() { MaxParallelism = 2, BatchSize = 100, PollIntervalSeconds = 1 };
            FakeTimeProvider timeProvider = new();

            Sut = new ParseOrchestrator(
                provider.GetRequiredService<IServiceScopeFactory>(),
                FileSystem,
                throwingParser,
                MEOptions.Create(parserOptions),
                timeProvider,
                NullLogger<ParseOrchestrator>.Instance);
        }

        public Guid AddSource(string path, string contentHash, string content)
        {
            Guid id = Guid.NewGuid();
            string normalized = path.Replace('/', Path.DirectorySeparatorChar);
            FileSystem.AddFile(normalized, content);
            Source.Sources.Add(new MarkdownSourceSnapshot(id, normalized, contentHash));
            return id;
        }
    }

    private sealed class ContentBasedThrowingParser : IMarkdownParser
    {
        private readonly IMarkdownParser _inner;
        private readonly string _failingContent;
        private readonly Exception _failure;

        public ContentBasedThrowingParser(IMarkdownParser inner, string failingContent, Exception failure)
        {
            _inner = inner;
            _failingContent = failingContent;
            _failure = failure;
        }

        public Parser.Models.ParseResult Parse(string markdownText)
        {
            if (string.Equals(markdownText, _failingContent, StringComparison.Ordinal))
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_failure).Throw();
            }
            return _inner.Parse(markdownText);
        }
    }
}
