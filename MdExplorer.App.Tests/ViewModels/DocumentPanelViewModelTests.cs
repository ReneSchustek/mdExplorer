using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Text;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Models;
using MdExplorer.Parser.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>Unit-Tests des <see cref="DocumentPanelViewModel"/>.</summary>
public sealed class DocumentPanelViewModelTests
{
    private const string TestPath = @"C:\notes\datei.md";
    private static readonly DateTime FixedUtc = new(2026, 6, 12, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task LoadAsync_OnExistingDocument_LoadsPreviewAndEditor()
    {
        Guid fileId = Guid.NewGuid();
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("# Titel");
        FakeMarkdownDocumentRepository repo = new();
        MarkdownDocument document = CreateDocument(fileId, "<h1>Titel</h1>");
        repo.Put(fileId, document);
        FakeDocumentLocator locator = new();
        locator.SetPath(fileId, TestPath);
        FakeMarkdownParser parser = new();
        using DocumentPanelViewModel sut = CreateSut(fs, repo, locator, parser);

        await sut.LoadAsync(fileId, CancellationToken.None).ConfigureAwait(true);

        Assert.Contains("<h1>Titel</h1>", sut.Preview.Html, StringComparison.Ordinal);
        Assert.Equal(fileId, sut.Preview.CurrentDocumentId);
        Assert.Equal(TestPath, sut.Editor.FilePath);
        Assert.Equal(fileId, sut.Editor.MarkdownFileId);
    }

    [Fact]
    public async Task LoadByPathAsync_OnIndexedFile_DelegatesToLoadAsync()
    {
        Guid fileId = Guid.NewGuid();
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("# Body");
        FakeMarkdownDocumentRepository repo = new();
        repo.Put(fileId, CreateDocument(fileId, "<p>Indexed</p>"));
        FakeDocumentLocator locator = new();
        locator.SetIndexedPath(TestPath, fileId);
        locator.SetPath(fileId, TestPath);
        FakeMarkdownParser parser = new();
        using DocumentPanelViewModel sut = CreateSut(fs, repo, locator, parser);

        await sut.LoadByPathAsync(TestPath, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(fileId, sut.Editor.MarkdownFileId);
        Assert.Equal(fileId, sut.Preview.CurrentDocumentId);
        Assert.Contains("<p>Indexed</p>", sut.Preview.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadByPathAsync_OnUnindexedFile_FallsBackToDirectLoad()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("# Direct");
        FakeMarkdownDocumentRepository repo = new();
        FakeDocumentLocator locator = new(); // FindByAbsolutePathAsync → null
        FakeMarkdownParser parser = new();
        parser.SetParseResult("<p>Direct</p>");
        using DocumentPanelViewModel sut = CreateSut(fs, repo, locator, parser);

        await sut.LoadByPathAsync(TestPath, CancellationToken.None).ConfigureAwait(true);

        Assert.Contains("<p>Direct</p>", sut.Preview.Html, StringComparison.Ordinal);
        Assert.Equal(Guid.Empty, sut.Editor.MarkdownFileId);
        Assert.Equal(TestPath, sut.Editor.FilePath);
    }

    [Fact]
    public async Task LoadDirectFromFileAsync_OnMissingFile_LogsAndReturns()
    {
        FakeFileSystem fs = new(); // keine Datei
        FakeMarkdownDocumentRepository repo = new();
        FakeDocumentLocator locator = new();
        FakeMarkdownParser parser = new();
        using DocumentPanelViewModel sut = CreateSut(fs, repo, locator, parser);

        await sut.LoadByPathAsync(TestPath, CancellationToken.None).ConfigureAwait(true);

        Assert.Null(sut.Editor.FilePath);
        Assert.Equal(0, parser.ParseCount);
    }

    [Fact]
    public async Task OnEditorSaved_RefreshesPreviewFromSavedText()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("alt");
        FakeMarkdownDocumentRepository repo = new();
        FakeDocumentLocator locator = new();
        FakeMarkdownParser parser = new();
        parser.SetParseResult("<p>nach Save</p>");
        using DocumentPanelViewModel sut = CreateSut(fs, repo, locator, parser);

        await sut.Editor.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);
        sut.Editor.EnterEditMode();
        sut.Editor.Text = "neuer Editor-Text";
        await sut.Editor.SaveAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Contains("<p>nach Save</p>", sut.Preview.Html, StringComparison.Ordinal);
        Assert.True(parser.ParseCount >= 1, "Parser muss nach Save mindestens einmal aufgerufen werden.");
        Assert.Equal("neuer Editor-Text", parser.LastInput);
    }

    [Fact]
    public void ToggleMode_FlipsBetweenReadAndEdit()
    {
        FakeFileSystem fs = new();
        using DocumentPanelViewModel sut = CreateSut(fs, new FakeMarkdownDocumentRepository(), new FakeDocumentLocator(), new FakeMarkdownParser());

        Assert.Equal(DocumentPanelMode.Read, sut.Mode);
        Assert.True(sut.IsReadMode);

        sut.ToggleMode();
        Assert.Equal(DocumentPanelMode.Edit, sut.Mode);
        Assert.True(sut.IsEditMode);

        sut.ToggleMode();
        Assert.Equal(DocumentPanelMode.Read, sut.Mode);
        Assert.True(sut.IsReadMode);
    }

    [Fact]
    public async Task Dispose_DisposesEditorAndUnsubscribesFromSaved()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("alt");
        FakeMarkdownDocumentRepository repo = new();
        FakeDocumentLocator locator = new();
        FakeMarkdownParser parser = new();
        DocumentPanelViewModel sut = CreateSut(fs, repo, locator, parser);

        await sut.Editor.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);
        sut.Editor.EnterEditMode();
        sut.Editor.Text = "geaendert";
        await sut.Editor.SaveAsync(CancellationToken.None).ConfigureAwait(true);
        int parseCountAfterFirstSave = parser.ParseCount;

        sut.Dispose();
        sut.Dispose(); // idempotent

        // Nach Dispose: Editor.Saved ist nicht mehr abonniert. Ein weiterer Save (mit erneut entsperrtem Editor)
        // darf den DocumentPanel.OnEditorSaved-Handler nicht mehr triggern.
        sut.Editor.EnterEditMode();
        sut.Editor.Text = "nochmal geaendert";
        await sut.Editor.SaveAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(parseCountAfterFirstSave, parser.ParseCount);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "Editor + ServiceProvider werden durch den DocumentPanelViewModel-Lebenszyklus gehalten und vom Test ueber dessen using-Statement disposed.")]
    private static DocumentPanelViewModel CreateSut(
        FakeFileSystem fs,
        FakeMarkdownDocumentRepository repo,
        FakeDocumentLocator locator,
        FakeMarkdownParser parser)
    {
        PreviewHtmlBuilder builder = new(new FakeThemeProvider(isDarkMode: false));
        ServiceProvider provider = BuildProvider(repo);
        PreviewViewModel preview = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            builder,
            NullLogger<PreviewViewModel>.Instance);
        MarkdownEditorViewModel editor = new(
            fs,
            new TagExtractor(new StubSettingsService()),
            TimeProvider.System,
            NullLogger<MarkdownEditorViewModel>.Instance);
        return new DocumentPanelViewModel(
            preview,
            editor,
            parser,
            builder,
            locator,
            fs,
            NullLogger<DocumentPanelViewModel>.Instance);
    }

    private static ServiceProvider BuildProvider(FakeMarkdownDocumentRepository repo)
    {
        ServiceCollection services = new();
        _ = services.AddScoped<IMarkdownDocumentRepository>(_ => repo);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static MarkdownDocument CreateDocument(Guid fileId, string body)
    {
        MarkdownDocument document = new()
        {
            Id = Guid.NewGuid(),
            MarkdownFileId = fileId,
            SourceContentHash = "hash",
            FrontmatterJson = "{}",
            OutlinksJson = "[]",
            ParsedAtUtc = FixedUtc,
        };
        document.SetRenderedHtmlGz(Gzip(body));
        return document;
    }

    private static byte[] Gzip(string text)
    {
        using MemoryStream output = new();
        using (GZipStream gz = new(output, CompressionLevel.Fastest))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            gz.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }

    private static byte[] GzipBytes(string text) => Gzip(text);

    private sealed class FakeDocumentLocator : IDocumentLocator
    {
        private readonly Dictionary<string, Guid> _indexedPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, string> _absolutePaths = [];

        public void SetIndexedPath(string absolutePath, Guid id) => _indexedPaths[absolutePath] = id;

        public void SetPath(Guid id, string absolutePath) => _absolutePaths[id] = absolutePath;

        public Task<Guid?> FindByWikiLinkAsync(string wikiLinkTarget, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(null);

        public Task<Guid?> FindByAbsolutePathAsync(string absoluteFilePath, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(_indexedPaths.TryGetValue(absoluteFilePath, out Guid id) ? id : null);

        public Task<string?> GetAbsolutePathAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(_absolutePaths.TryGetValue(markdownFileId, out string? path) ? path : null);
    }

    private sealed class FakeMarkdownParser : IMarkdownParser
    {
        private string _bodyHtml = string.Empty;

        public int ParseCount { get; private set; }
        public string? LastInput { get; private set; }

        public void SetParseResult(string bodyHtml) => _bodyHtml = bodyHtml;

        public ParseResult Parse(string markdownText)
        {
            ParseCount++;
            LastInput = markdownText;
            return new ParseResult(
                new Dictionary<string, string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                GzipBytes(_bodyHtml));
        }
    }

    private sealed class StubSettingsService : ISettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Default;
        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            AppSettings previous = Current;
            Current = settings;
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(previous, settings));
            return Task.CompletedTask;
        }
    }
}
