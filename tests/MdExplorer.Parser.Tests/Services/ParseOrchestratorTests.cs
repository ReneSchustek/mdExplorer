using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Diagnostics;
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
/// Der Orchestrator parst nur neue oder geänderte Dateien, legt Schlagworte je Slug einmal an,
/// lädt vorhandene Dokumente gesammelt und lässt sich von einer einzelnen unlesbaren oder
/// unparsbaren Datei nicht aufhalten. Bräche er an einer Datei ab, bliebe der ganze übrige
/// Bestand ungeparst.
/// </summary>
public sealed class ParseOrchestratorTests
{
    [Fact]
    public async Task RunOnce_OnMissingDocument_PersistsNewEntry()
    {
        using OrchestratorHarness harness = new();
        Guid fileId = harness.AddSource("/r/foo.md", "abchash", "# Titel\n\nBody mit #foo.");

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, harness.DocRepo.SaveCallCount);
        Assert.True(harness.DocRepo.Snapshot.ContainsKey(fileId));
        Assert.Equal("abchash", harness.DocRepo.Snapshot[fileId].SourceContentHash);
    }

    [Fact]
    public async Task RunOnce_OnUnchangedHash_DoesNotReParse()
    {
        using OrchestratorHarness harness = new();
        _ = harness.AddSource("/r/foo.md", "h1", "Body #foo");

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);
        int writesAfterFirst = harness.DocRepo.ParseCount;

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(writesAfterFirst, harness.DocRepo.ParseCount);
    }

    [Fact]
    public async Task RunOnce_OnChangedHash_UpdatesExistingDocument()
    {
        using OrchestratorHarness harness = new();
        Guid fileId = harness.AddSource("/r/foo.md", "h1", "Body #foo");
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        harness.UpdateSource(fileId, "h2", "Body #bar");
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal("h2", harness.DocRepo.Snapshot[fileId].SourceContentHash);
        Assert.True(harness.TagRepo.TagsBySlug.ContainsKey("bar"));
        _ = Assert.Single(harness.TagRepo.FileLinks[fileId]);
    }

    [Fact]
    public async Task RunOnce_OnReadFailure_SkipsFileWithoutCrash()
    {
        using OrchestratorHarness harness = new();
        Guid bad = harness.AddSource("/r/bad.md", "h1", "ignored");
        Guid good = harness.AddSource("/r/good.md", "h1", "Body #good");
        _ = harness.FileSystem.FailOnRead.Add("/r/bad.md".Replace('/', Path.DirectorySeparatorChar));

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.False(harness.DocRepo.Snapshot.ContainsKey(bad));
        Assert.True(harness.DocRepo.Snapshot.ContainsKey(good));
    }

    [Fact]
    public async Task RunOnce_OnFrontmatterTagsAndBodyTags_CreatesAllSlugs()
    {
        using OrchestratorHarness harness = new();
        Guid fileId = harness.AddSource("/r/f.md", "h1", """
            ---
            tags: [Alpha, Beta]
            ---
            Body mit #gamma.
            """);

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Contains("alpha", harness.TagRepo.TagsBySlug.Keys, StringComparer.Ordinal);
        Assert.Contains("beta", harness.TagRepo.TagsBySlug.Keys, StringComparer.Ordinal);
        Assert.Contains("gamma", harness.TagRepo.TagsBySlug.Keys, StringComparer.Ordinal);
        Assert.Equal(3, harness.TagRepo.FileLinks[fileId].Count);
    }

    [Fact]
    public async Task RunOnce_ReusesExistingTags_DoesNotDuplicate()
    {
        using OrchestratorHarness harness = new();
        _ = harness.AddSource("/r/a.md", "h1", "#shared");
        _ = harness.AddSource("/r/b.md", "h1", "#shared");

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        _ = Assert.Single(harness.TagRepo.TagsBySlug);
        Assert.True(harness.TagRepo.TagsBySlug.ContainsKey("shared"));
    }

    // Was der Parser an einer Datei nicht verkraftet, kostet nur diese Datei. Markdig wirft bei zu
    // tief verschachtelten Eingaben eine ArgumentException; für Eingaben, die niemand vorhersieht,
    // steht eine FormatException. Ohne Abfangen je Datei beendete die erste solche Datei den
    // ganzen Durchlauf, und der Wächter legte danach den Dienst still.
    [Theory]
    [InlineData("argument")]
    [InlineData("invalid-operation")]
    [InlineData("format")]
    public async Task RunOnce_OnAParserFailure_SkipsTheFileAndContinuesWithTheRest(string kind)
    {
        const string BadContent = "PAYLOAD_THE_PARSER_REJECTS";
        Exception failure = kind switch
        {
            "argument" => new ArgumentException("depth limit exceeded"),
            "invalid-operation" => new InvalidOperationException("kaputter Zustand"),
            _ => new FormatException("unerwartet"),
        };
        using OrchestratorHarness harness = new(new ContentBasedThrowingParser(OrchestratorHarness.CreateParser(), BadContent, failure));
        Guid badId = harness.AddSource("/r/bad.md", "h1", BadContent);
        Guid goodId = harness.AddSource("/r/good.md", "h1", "Body #ok.");

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.False(harness.DocRepo.Snapshot.ContainsKey(badId));
        Assert.True(harness.DocRepo.Snapshot.ContainsKey(goodId));
        Assert.True(harness.TagRepo.TagsBySlug.ContainsKey("ok"));
        Assert.True(harness.FailureRepo.Snapshot.ContainsKey(badId));
    }

    // Zweite Linie: Kommt eine ArgumentException außerhalb des Parsens einer Datei auf, hier beim
    // Aufzählen der Quellen, fängt TryRunOnceAsync sie ab, und der nächste Takt läuft normal.
    [Fact]
    public async Task TryRunOnce_OnUnexpectedArgumentException_LogsAndContinues()
    {
        using OrchestratorHarness harness = new();
        harness.Source.ThrowOnNextEnumeration = new ArgumentException("simulated depth-limit-bubble");

        await harness.Sut.TryRunOnceAsync(TestContext.Current.CancellationToken);

        // Der nächste Takt läuft normal.
        _ = harness.AddSource("/r/good.md", "h1", "#weiter");
        await harness.Sut.TryRunOnceAsync(TestContext.Current.CancellationToken);

        Assert.True(harness.TagRepo.TagsBySlug.ContainsKey("weiter"));
    }

    [Fact]
    public async Task TryRunOnce_OnUnexpectedInvalidOperationException_LogsAndContinues()
    {
        using OrchestratorHarness harness = new();
        harness.Source.ThrowOnNextEnumeration = new InvalidOperationException("simulated parser state");

        await harness.Sut.TryRunOnceAsync(TestContext.Current.CancellationToken);

        _ = harness.AddSource("/r/good.md", "h1", "#weiter");
        await harness.Sut.TryRunOnceAsync(TestContext.Current.CancellationToken);

        Assert.True(harness.TagRepo.TagsBySlug.ContainsKey("weiter"));
    }

    // Zwei Dateien mit demselben neuen Schlagwort im selben Stapel. Legte jede Datei es selbst an,
    // scheiterte SaveChanges mit "UNIQUE constraint failed: Tags.Slug".
    [Fact]
    public async Task ProcessBatch_OnTwoFilesSharingNewTag_DoesNotThrowUniqueConstraint()
    {
        using OrchestratorHarness harness = new();
        Guid a = harness.AddSource("/r/a.md", "h1", "Body mit #shared.");
        Guid b = harness.AddSource("/r/b.md", "h1", "Anderes Body mit #shared!");

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        _ = Assert.Single(harness.TagRepo.TagsBySlug);
        Assert.True(harness.TagRepo.TagsBySlug.ContainsKey("shared"));
        Guid linkA = Assert.Single(harness.TagRepo.FileLinks[a]);
        Guid linkB = Assert.Single(harness.TagRepo.FileLinks[b]);
        Assert.Equal(linkA, linkB);
    }

    // ProcessBatchAsync lädt die vorhandenen Dokumente einmal gesammelt. Eine Abfrage je Datei über
    // GetByMarkdownFileIdAsync wäre ein N+1; der Zähler muss bei 0 bleiben.
    [Fact]
    public async Task ProcessBatch_LoadsExistingDocumentsInSingleBatch_WithoutPerFilePointLookup()
    {
        using OrchestratorHarness harness = new();
        _ = harness.AddSource("/r/a.md", "h1", "Body #a");
        _ = harness.AddSource("/r/b.md", "h1", "Body #b");
        _ = harness.AddSource("/r/c.md", "h1", "Body #c");

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, harness.DocRepo.PointLookupCallCount);
        Assert.Equal(1, harness.DocRepo.BatchLookupCallCount);
        Assert.Equal(3, harness.DocRepo.Snapshot.Count);
    }

    // Das gesammelte Laden speist auch das Ändern: Ein vorhandenes Dokument wird dort gefunden und
    // aktualisiert statt doppelt angelegt.
    [Fact]
    public async Task ProcessBatch_OnExistingDocument_UpdatesViaBatchLoadedEntry()
    {
        using OrchestratorHarness harness = new();
        Guid fileId = harness.AddSource("/r/foo.md", "h1", "Body #foo");
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        harness.UpdateSource(fileId, "h2", "Body #bar");
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, harness.DocRepo.PointLookupCallCount);
        _ = Assert.Single(harness.DocRepo.Snapshot);
        Assert.Equal("h2", harness.DocRepo.Snapshot[fileId].SourceContentHash);
    }

    [Fact]
    public async Task RunOnce_StoresGzippedHtmlBlob()
    {
        using OrchestratorHarness harness = new();
        Guid fileId = harness.AddSource("/r/f.md", "h1", "# Titel\n\nText.");

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        ReadOnlyMemory<byte> blob = harness.DocRepo.Snapshot[fileId].RenderedHtmlGz;
        string html = GzipHelper.Decompress(blob);
        Assert.Contains("<h1", html, StringComparison.Ordinal);
    }
}
