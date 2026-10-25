using System.IO;
using System.Text;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Graph.Abstractions;
using MdExplorer.Graph.Models;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Models;
using MdExplorer.Parser.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Baut den Dokumentbereich mit Vorschau, Editor und Bezügen auf Attrappen auf und gibt beim
/// Entsorgen alles wieder frei, was er dafür erzeugt hat.
/// </summary>
/// <remarks>
/// Die Attrappen sind veränderlich: Ein Test legt Dateien, Dokumente und Pfade an, bevor oder
/// nachdem der Bereich steht. Die Bezüge laufen gegen einen Graphen ohne Verbindungen; ihr Inhalt
/// hat eigene Tests, hier zählt nur, zu welchem Dokument sie gehören.
/// </remarks>
internal sealed class DocumentPanelHarness : IDisposable
{
    private static readonly DateTime FixedUtc = new(2026, 6, 12, 8, 0, 0, DateTimeKind.Utc);

    private readonly ServiceProvider _provider;

    public DocumentPanelHarness(FakeUnsavedChangesDialogService? unsavedChangesDialog = null)
    {
        ServiceCollection services = new();
        _ = services.AddScoped<IMarkdownDocumentRepository>(_ => Repository);
        _ = services.AddScoped<IMarkdownFileRepository>(_ => Files);
        _ = services.AddScoped<IGraphService>(_ => new UnconnectedGraph());
        _provider = services.BuildServiceProvider(validateScopes: true);

        IServiceScopeFactory scopes = _provider.GetRequiredService<IServiceScopeFactory>();
        PreviewHtmlBuilder builder = new(new FakeThemeProvider(isDarkMode: false), new FakeSettingsService(AppSettings.Default));
        Editor = TestEditors.Create(FileSystem);
        Sut = new DocumentPanelViewModel(
            new PreviewViewModel(scopes, builder, NullLogger<PreviewViewModel>.Instance),
            Editor,
            new DocumentRelationsViewModel(scopes, new StubDocumentFileService(), new FakeDialogService(), new StubUnsavedChangesGuard(), NullLogger<DocumentRelationsViewModel>.Instance),
            Parser,
            Locator,
            FileSystem,
            new UnsavedChangesGuard(Editor, unsavedChangesDialog ?? new FakeUnsavedChangesDialogService()),
            NullLogger<DocumentPanelViewModel>.Instance);
    }

    public FakeFileSystem FileSystem { get; } = new();

    public FakeMarkdownDocumentRepository Repository { get; } = new();

    public FakeMarkdownFileRepository Files { get; } = new();

    public FakeDocumentLocator Locator { get; } = new();

    public FakeMarkdownParser Parser { get; } = new();

    public MarkdownEditorViewModel Editor { get; }

    public DocumentPanelViewModel Sut { get; }

    /// <summary>Legt ein indiziertes Dokument an: Datei auf der Platte, gerendertes HTML und Pfad im Index.</summary>
    public void AddIndexed(Guid id, string absolutePath, string markdown, string html)
    {
        FileSystem.Files[absolutePath] = Encoding.UTF8.GetBytes(markdown);
        Repository.Put(id, CreateDocument(id, html));
        Locator.SetPath(id, absolutePath);
        Locator.SetIndexedPath(absolutePath, id);
        Files.Add(new MarkdownFile
        {
            Id = id,
            AbsolutePath = absolutePath,
            RelativePath = Path.GetFileName(absolutePath),
            FileNameWithoutExtension = Path.GetFileNameWithoutExtension(absolutePath),
            ContentHash = "hash",
        });
    }

    public static MarkdownDocument CreateDocument(Guid fileId, string body)
    {
        MarkdownDocument document = new()
        {
            Id = new Guid("abababab-0000-0000-0000-000000000005"),
            MarkdownFileId = fileId,
            SourceContentHash = "hash",
            FrontmatterJson = "{}",
            OutlinksJson = "[]",
            ParsedAtUtc = FixedUtc,
        };
        document.SetRenderedHtmlGz(TestGzip.Compress(body));
        return document;
    }

    public void Dispose()
    {
        Sut.Dispose();
        Editor.Dispose();
        _provider.Dispose();
    }

    /// <summary>Ein Graph ohne Verbindungen.</summary>
    private sealed class UnconnectedGraph : IGraphService
    {
        public Task<GraphSnapshot> BuildSnapshotAsync(GraphFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult(GraphSnapshot.Empty);

        public Task<DocumentRelations> GetRelationsAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
            Task.FromResult(DocumentRelations.Empty);
    }
}

/// <summary>Auflösung von Pfad und Kennung aus vorgegebenen Tabellen; kennt keine WikiLinks.</summary>
internal sealed class FakeDocumentLocator : IDocumentLocator
{
    private readonly Dictionary<string, Guid> _indexedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, string> _absolutePaths = [];
    private readonly Dictionary<Guid, TaskCompletionSource> _gates = [];

    /// <summary>Der Fehler, den die Pfad-Auflösung meldet, für die Datenbank-Spitze.</summary>
    public Exception? FailOnGetPath { get; set; }

    public void SetIndexedPath(string absolutePath, Guid id) => _indexedPaths[absolutePath] = id;

    public void SetPath(Guid id, string absolutePath) => _absolutePaths[id] = absolutePath;

    /// <summary>Hält die Pfad-Auflösung einer Kennung an, bis der Test sie freigibt.</summary>
    /// <remarks>So trifft die Antwort eines früheren Klicks nach der eines späteren ein.</remarks>
    public TaskCompletionSource Hold(Guid markdownFileId)
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _gates[markdownFileId] = gate;
        return gate;
    }

    public Task<Guid?> FindByWikiLinkAsync(string wikiLinkTarget, CancellationToken cancellationToken) =>
        Task.FromResult<Guid?>(null);

    public Task<Guid?> FindByAbsolutePathAsync(string absoluteFilePath, CancellationToken cancellationToken) =>
        Task.FromResult<Guid?>(_indexedPaths.TryGetValue(absoluteFilePath, out Guid id) ? id : null);

    public async Task<string?> GetAbsolutePathAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        if (_gates.TryGetValue(markdownFileId, out TaskCompletionSource? gate))
        {
            await gate.Task.ConfigureAwait(false);
        }
        if (FailOnGetPath is not null)
        {
            throw FailOnGetPath;
        }
        return _absolutePaths.TryGetValue(markdownFileId, out string? path) ? path : null;
    }
}

/// <summary>Parser mit vorgegebenem HTML, der Aufrufe und letzte Eingabe festhält.</summary>
internal sealed class FakeMarkdownParser : IMarkdownParser
{
    private string _bodyHtml = string.Empty;

    public string EngineVersion => "test-engine/1";

    public int ParseCount { get; private set; }

    public string? LastInput { get; private set; }

    /// <summary>Der Fehler, den das Übersetzen meldet, für ein Dokument, das Markdig abweist.</summary>
    public Exception? FailOnParse { get; set; }

    public void SetParseResult(string bodyHtml) => _bodyHtml = bodyHtml;

    public ParseResult Parse(string markdownText)
    {
        ParseCount++;
        LastInput = markdownText;
        if (FailOnParse is not null)
        {
            throw FailOnParse;
        }

        return new ParseResult(
            new Dictionary<string, string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            TestGzip.Compress(_bodyHtml));
    }
}
