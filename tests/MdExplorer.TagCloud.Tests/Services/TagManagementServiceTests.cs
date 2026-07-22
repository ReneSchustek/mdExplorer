using System.Diagnostics;
using System.IO;
using System.Text;
using MdExplorer.Core.Abstractions;
using MdExplorer.Parser.Services;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.TagCloud.Tests.Services;

/// <summary>
/// Unit-Tests: Rename / Merge / Delete inkl. Frontmatter- und Body-Schreibstrategie.
/// Sowie Integrationstest fuer projektweites Rename in 100 Dateien (Performance-Budget &lt; 5 s).
/// </summary>
public sealed class TagManagementServiceTests
{
    [Fact]
    public async Task RenameAsync_OnBodyTag_RewritesAllOccurrences()
    {
        InMemoryFileSystem fs = new();
        fs.AddFile(@"C:\notes\a.md", "Body mit #foo Tag.");
        fs.AddFile(@"C:\notes\b.md", "Mehr #foo und mehr.");
        FakeTagFileLookupQuery query = new();
        query.SetFiles("foo",
            new TagFileLookupRow(Guid.NewGuid(), @"C:\notes\a.md", "a.md"),
            new TagFileLookupRow(Guid.NewGuid(), @"C:\notes\b.md", "b.md"));
        TagManagementService sut = CreateSut(query, fs);

        TagRewriteResult result = await sut.RenameAsync("foo", "bar", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("foo", result.Slug);
        Assert.Equal(2, result.FilesAffected);
        Assert.Equal(2, result.FilesAttempted);
        Assert.Empty(result.Errors);
        Assert.Equal("Body mit #bar Tag.", fs.ReadText(@"C:\notes\a.md"));
        Assert.Equal("Mehr #bar und mehr.", fs.ReadText(@"C:\notes\b.md"));
    }

    [Fact]
    public async Task RenameAsync_OnFrontmatterTags_RewritesYamlList()
    {
        InMemoryFileSystem fs = new();
        const string Input = "---\ntitle: x\ntags:\n  - foo\n  - keep\n---\nBody.\n";
        fs.AddFile(@"C:\notes\a.md", Input);
        FakeTagFileLookupQuery query = new();
        query.SetFiles("foo", new TagFileLookupRow(Guid.NewGuid(), @"C:\notes\a.md", "a.md"));
        TagManagementService sut = CreateSut(query, fs);

        TagRewriteResult result = await sut.RenameAsync("foo", "neu", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, result.FilesAffected);
        Assert.Equal(
            "---\ntitle: x\ntags:\n  - neu\n  - keep\n---\nBody.\n",
            fs.ReadText(@"C:\notes\a.md"));
    }

    [Fact]
    public async Task MergeAsync_RewritesAndDedupesInline()
    {
        InMemoryFileSystem fs = new();
        const string Input = "---\ntags: [source, target]\n---\nBody mit #source und #target.\n";
        fs.AddFile(@"C:\notes\a.md", Input);
        FakeTagFileLookupQuery query = new();
        query.SetFiles("source", new TagFileLookupRow(Guid.NewGuid(), @"C:\notes\a.md", "a.md"));
        TagManagementService sut = CreateSut(query, fs);

        TagRewriteResult result = await sut.MergeAsync("source", "target", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, result.FilesAffected);
        Assert.Equal(
            "---\ntags: [target]\n---\nBody mit #target und #target.\n",
            fs.ReadText(@"C:\notes\a.md"));
    }

    [Fact]
    public async Task DeleteAsync_RemovesBodyAndFrontmatter()
    {
        InMemoryFileSystem fs = new();
        const string Input = "---\ntags:\n  - keep\n  - kill\n---\nBefore #kill after.\n";
        fs.AddFile(@"C:\notes\a.md", Input);
        FakeTagFileLookupQuery query = new();
        query.SetFiles("kill", new TagFileLookupRow(Guid.NewGuid(), @"C:\notes\a.md", "a.md"));
        TagManagementService sut = CreateSut(query, fs);

        TagRewriteResult result = await sut.DeleteAsync("kill", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, result.FilesAffected);
        Assert.Equal(
            "---\ntags:\n  - keep\n---\nBefore  after.\n",
            fs.ReadText(@"C:\notes\a.md"));
    }

    [Fact]
    public async Task GetPreviewAsync_LimitsSampleListToTen()
    {
        InMemoryFileSystem fs = new();
        FakeTagFileLookupQuery query = new();
        TagFileLookupRow[] rows = new TagFileLookupRow[25];
        for (int index = 0; index < rows.Length; index++)
        {
            rows[index] = new TagFileLookupRow(Guid.NewGuid(), $@"C:\notes\file-{index:D2}.md", $"file-{index:D2}.md");
        }
        query.SetFiles("foo", rows);
        TagManagementService sut = CreateSut(query, fs);

        TagPreview preview = await sut.GetPreviewAsync("foo", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(25, preview.FileCount);
        Assert.Equal(10, preview.SamplePaths.Count);
    }

    [Fact]
    public async Task RenameAsync_OnNoFiles_ReturnsZeroAffected()
    {
        InMemoryFileSystem fs = new();
        FakeTagFileLookupQuery query = new();
        TagManagementService sut = CreateSut(query, fs);

        TagRewriteResult result = await sut.RenameAsync("missing", "neu", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(0, result.FilesAffected);
        Assert.Equal(0, result.FilesAttempted);
    }

    [Fact]
    public async Task RenameAsync_OnFileWithoutOccurrence_CountsAsUnchanged()
    {
        InMemoryFileSystem fs = new();
        // Datei wird laut Index zwar referenziert, im aktuellen Text aber nicht mehr — z. B. weil
        // sie zwischen Index-Lauf und Operation veraendert wurde. Soll keinen Schreib-Roundtrip ausloesen.
        fs.AddFile(@"C:\notes\a.md", "Kein passender Hashtag im Body.");
        FakeTagFileLookupQuery query = new();
        query.SetFiles("foo", new TagFileLookupRow(Guid.NewGuid(), @"C:\notes\a.md", "a.md"));
        TagManagementService sut = CreateSut(query, fs);

        TagRewriteResult result = await sut.RenameAsync("foo", "bar", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(0, result.FilesAffected);
        Assert.Equal(1, result.FilesAttempted);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task RenameAsync_OnHundredFiles_CompletesUnderFiveSeconds()
    {
        InMemoryFileSystem fs = new();
        FakeTagFileLookupQuery query = new();
        const int FileCount = 100;
        TagFileLookupRow[] rows = new TagFileLookupRow[FileCount];
        for (int index = 0; index < FileCount; index++)
        {
            string path = $@"C:\notes\bulk-{index:D3}.md";
            string content = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"---\ntitle: doc{index}\ntags:\n  - foo\n  - keep\n---\nBody {index} mit #foo Tag.\n");
            fs.AddFile(path, content);
            rows[index] = new TagFileLookupRow(Guid.NewGuid(), path, $"bulk-{index:D3}.md");
        }
        query.SetFiles("foo", rows);
        TagManagementService sut = CreateSut(query, fs);

        Stopwatch stopwatch = Stopwatch.StartNew();
        TagRewriteResult result = await sut.RenameAsync("foo", "neu", CancellationToken.None).ConfigureAwait(true);
        stopwatch.Stop();

        Assert.Equal(FileCount, result.FilesAffected);
        Assert.Empty(result.Errors);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Projektweites Rename benoetigte {stopwatch.Elapsed.TotalSeconds:F2}s — Budget ist 5 s.");
        for (int index = 0; index < FileCount; index++)
        {
            string actual = fs.ReadText($@"C:\notes\bulk-{index:D3}.md");
            Assert.Contains("- neu", actual, StringComparison.Ordinal);
            Assert.Contains("#neu", actual, StringComparison.Ordinal);
            Assert.DoesNotContain("#foo", actual, StringComparison.Ordinal);
        }
    }

    private static TagManagementService CreateSut(FakeTagFileLookupQuery query, InMemoryFileSystem fs)
    {
        MarkdownTagRewriter rewriter = new(new TagNormalizer());
        return new TagManagementService(query, fs, rewriter, NullLogger<TagManagementService>.Instance);
    }
}

internal sealed class FakeTagFileLookupQuery : ITagFileLookupQuery
{
    private readonly Dictionary<string, IReadOnlyList<TagFileLookupRow>> _files = new(StringComparer.Ordinal);

    public void SetFiles(string slug, params TagFileLookupRow[] rows) =>
        _files[slug] = rows;

    public Task<IReadOnlyList<TagFileLookupRow>> GetFilesByTagSlugAsync(string slug, CancellationToken cancellationToken)
    {
        if (_files.TryGetValue(slug, out IReadOnlyList<TagFileLookupRow>? rows))
        {
            return Task.FromResult(rows);
        }
        return Task.FromResult<IReadOnlyList<TagFileLookupRow>>([]);
    }
}

internal sealed class InMemoryFileSystem : IFileSystem
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    public void AddFile(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _files[path] = Utf8NoBom.GetBytes(content);
    }

    public string ReadText(string path) => Utf8NoBom.GetString(_files[path]);

    public bool DirectoryExists(string path) => false;
    public bool FileExists(string path) => _files.ContainsKey(path);
    public void EnsureDirectoryExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
    }

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) => [];

    public IEnumerable<string> EnumerateDirectories(string directory) => [];

    public bool IsReparsePoint(string path) => false;

    public string GetDirectoryFinalPath(string path) => path;

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(_files[path]);

    public byte[] ReadAllBytes(string path) => _files[path];

    public Stream OpenRead(string path) => new MemoryStream(_files[path], writable: false);

    public DateTime GetLastWriteTimeUtc(string path) => DateTime.UnixEpoch;

    public long GetFileSize(string path) => _files[path].LongLength;

    public Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _files[path] = content.ToArray();
        return Task.CompletedTask;
    }
}
