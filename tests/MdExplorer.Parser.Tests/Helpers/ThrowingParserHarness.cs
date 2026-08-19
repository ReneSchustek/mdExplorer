using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Diagnostics;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Options;
using MdExplorer.Parser.Services;
using MdExplorer.Parser.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MEOptions = Microsoft.Extensions.Options.Options;

namespace MdExplorer.Parser.Tests.Helpers;

/// <summary>
/// Umgebung mit einem Parser, der bei einem bestimmten Roh-Inhalt wirft. Damit lassen sich die
/// Fehlschlag-Pfade unabhängig vom Verhalten der jeweiligen Markdig-Fassung prüfen.
/// </summary>
internal sealed class ThrowingParserHarness
{
    public ThrowingParserHarness(string failingContent, Exception failure)
    {
        DocRepo.OnSaveChangesAsync = ct => TagRepo.SaveChangesAsync(ct);

        ServiceCollection services = new();
        _ = services.AddSingleton<IMarkdownSourceProvider>(Source);
        _ = services.AddSingleton<IMarkdownDocumentRepository>(DocRepo);
        _ = services.AddSingleton<ITagRepository>(TagRepo);
        _ = services.AddSingleton<IParseFailureRepository>(FailureRepo);
        ServiceProvider provider = services.BuildServiceProvider();

        MarkdigParser baseParser = new(
            new FrontmatterExtractor(),
            new TagExtractor(new FakeSettingsService()),
            new WikiLinkExtractor(),
            new TagNormalizer());
        Parser = new ContentBasedThrowingParser(baseParser, failingContent, failure);
        ParserOptions parserOptions = new() { MaxParallelism = 2, BatchSize = 100, PollIntervalSeconds = 1 };

        Sut = new ParseOrchestrator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            FileSystem,
            Parser,
            FailureStatus,
            MEOptions.Create(parserOptions),
            new FakeTimeProvider(),
            Logger);
    }

    public FakeFileSystem FileSystem { get; } = new();

    public FakeMarkdownSourceProvider Source { get; } = new();

    public FakeMarkdownDocumentRepository DocRepo { get; } = new();

    public FakeTagRepository TagRepo { get; } = new();

    public FakeParseFailureRepository FailureRepo { get; } = new();

    public ParseFailureStatus FailureStatus { get; } = new();

    public ContentBasedThrowingParser Parser { get; }

    public RecordingLogger<ParseOrchestrator> Logger { get; } = new();

    public ParseOrchestrator Sut { get; }

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
