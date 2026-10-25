using System.IO;
using System.Text;
using MdExplorer.Core.Abstractions;
using MdExplorer.Parser.Services;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Services;
using MdExplorer.TagCloud.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.TagCloud.Tests.Services;

/// <summary>
/// Umbenennen, Zusammenführen und Löschen eines Schlagworts schreibt alle betroffenen Dateien in
/// Frontmatter und Text um, fasst Dateien ohne Vorkommen nicht an und zeigt in der Vorschau höchstens
/// zehn Pfade. Ein Fehler hier verändert Dateien des Nutzers.
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
            new TagFileLookupRow(IdOf(1), @"C:\notes\a.md", "a.md"),
            new TagFileLookupRow(IdOf(2), @"C:\notes\b.md", "b.md"));
        TagManagementService sut = CreateSut(query, fs);

        TagRewriteResult result = await sut.RenameAsync("foo", "bar", TestContext.Current.CancellationToken).ConfigureAwait(true);

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
        query.SetFiles("foo", new TagFileLookupRow(IdOf(3), @"C:\notes\a.md", "a.md"));
        TagManagementService sut = CreateSut(query, fs);

        TagRewriteResult result = await sut.RenameAsync("foo", "neu", TestContext.Current.CancellationToken).ConfigureAwait(true);

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
        query.SetFiles("source", new TagFileLookupRow(IdOf(4), @"C:\notes\a.md", "a.md"));
        TagManagementService sut = CreateSut(query, fs);

        TagRewriteResult result = await sut.MergeAsync("source", "target", TestContext.Current.CancellationToken).ConfigureAwait(true);

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
        query.SetFiles("kill", new TagFileLookupRow(IdOf(5), @"C:\notes\a.md", "a.md"));
        TagManagementService sut = CreateSut(query, fs);

        TagRewriteResult result = await sut.DeleteAsync("kill", TestContext.Current.CancellationToken).ConfigureAwait(true);

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
            rows[index] = new TagFileLookupRow(IdOf(index + 600), $@"C:\notes\file-{index:D2}.md", $"file-{index:D2}.md");
        }
        query.SetFiles("foo", rows);
        TagManagementService sut = CreateSut(query, fs);

        TagPreview preview = await sut.GetPreviewAsync("foo", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(25, preview.FileCount);
        Assert.Equal(10, preview.SamplePaths.Count);
    }

    [Fact]
    public async Task RenameAsync_OnNoFiles_ReturnsZeroAffected()
    {
        InMemoryFileSystem fs = new();
        FakeTagFileLookupQuery query = new();
        TagManagementService sut = CreateSut(query, fs);

        TagRewriteResult result = await sut.RenameAsync("missing", "neu", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, result.FilesAffected);
        Assert.Equal(0, result.FilesAttempted);
    }

    [Fact]
    public async Task RenameAsync_OnFileWithoutOccurrence_CountsAsUnchanged()
    {
        InMemoryFileSystem fs = new();
        // Laut Index trägt die Datei das Schlagwort, im aktuellen Text aber nicht mehr, etwa weil sie
        // seit dem letzten Indexlauf bearbeitet wurde. Dann wird sie nicht neu geschrieben.
        fs.AddFile(@"C:\notes\a.md", "Kein passender Hashtag im Body.");
        FakeTagFileLookupQuery query = new();
        query.SetFiles("foo", new TagFileLookupRow(IdOf(7), @"C:\notes\a.md", "a.md"));
        TagManagementService sut = CreateSut(query, fs);

        TagRewriteResult result = await sut.RenameAsync("foo", "bar", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, result.FilesAffected);
        Assert.Equal(1, result.FilesAttempted);
    }

    [Fact]
    public async Task RenameAsync_OnHundredFiles_RewritesEveryFile()
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
            rows[index] = new TagFileLookupRow(IdOf(index + 800), path, $"bulk-{index:D3}.md");
        }
        query.SetFiles("foo", rows);
        TagManagementService sut = CreateSut(query, fs);

        TagRewriteResult result = await sut.RenameAsync("foo", "neu", TestContext.Current.CancellationToken).ConfigureAwait(true);

        // Die Zusicherung liegt auf der Vollständigkeit: Alle hundert Dateien sind angefasst,
        // keine mit Fehler, und der Inhalt stimmt. Eine Zeitgrenze mäße nur die Auslastung.
        Assert.Equal(FileCount, result.FilesAffected);
        Assert.Empty(result.Errors);
        for (int index = 0; index < FileCount; index++)
        {
            string actual = fs.ReadText($@"C:\notes\bulk-{index:D3}.md");
            Assert.Contains("- neu", actual, StringComparison.Ordinal);
            Assert.Contains("#neu", actual, StringComparison.Ordinal);
            Assert.DoesNotContain("#foo", actual, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("zwei worte")]
    [InlineData("a,b")]
    [InlineData("1zahl")]
    public async Task RenameAsync_WithAnInvalidName_ThrowsBeforeTouchingAFile(string name)
    {
        InMemoryFileSystem fs = new();
        fs.AddFile(@"C:\notes\a.md", "Body mit #foo Tag.");
        FakeTagFileLookupQuery query = new();
        query.SetFiles("foo", new TagFileLookupRow(IdOf(901), @"C:\notes\a.md", "a.md"));
        TagManagementService sut = CreateSut(query, fs);

        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => sut.RenameAsync("foo", name, TestContext.Current.CancellationToken)).ConfigureAwait(true);

        Assert.Equal("Body mit #foo Tag.", fs.ReadText(@"C:\notes\a.md"));
    }

    [Fact]
    public async Task RenameAsync_OnAFileWithByteOrderMark_KeepsTheMark()
    {
        InMemoryFileSystem fs = new();
        fs.AddBytes(@"C:\notes\a.md", [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("Body mit #foo Tag.")]);
        FakeTagFileLookupQuery query = new();
        query.SetFiles("foo", new TagFileLookupRow(IdOf(902), @"C:\notes\a.md", "a.md"));
        TagManagementService sut = CreateSut(query, fs);

        _ = await sut.RenameAsync("foo", "bar", TestContext.Current.CancellationToken).ConfigureAwait(true);

        byte[] written = fs.ReadBytes(@"C:\notes\a.md");
        Assert.True(written.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Equal("Body mit #bar Tag.", Encoding.UTF8.GetString(written.AsSpan(Encoding.UTF8.Preamble.Length)));
    }

    [Fact]
    public async Task RenameAsync_OnAFileThatIsNotUtf8_LeavesItUntouchedAndReportsIt()
    {
        InMemoryFileSystem fs = new();
        byte[] latin1 = Encoding.Latin1.GetBytes("Gr\u00fc\u00dfe mit #foo Tag.");
        fs.AddBytes(@"C:\notes\alt.md", latin1);
        FakeTagFileLookupQuery query = new();
        query.SetFiles("foo", new TagFileLookupRow(IdOf(903), @"C:\notes\alt.md", "alt.md"));
        TagManagementService sut = CreateSut(query, fs);

        TagRewriteResult result = await sut.RenameAsync("foo", "bar", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(latin1, fs.ReadBytes(@"C:\notes\alt.md"));
        _ = Assert.Single(result.Errors);
        Assert.Equal(0, result.FilesAffected);
    }

    [Fact]
    public async Task RenameAsync_WhenOneFileCannotBeWritten_ReportsItAndRewritesTheOthers()
    {
        InMemoryFileSystem fs = new();
        fs.AddFile(@"C:\notes\a.md", "Body mit #foo Tag.");
        fs.AddFile(@"C:\notes\b.md", "Mehr #foo und mehr.");
        fs.LockForWriting(@"C:\notes\a.md");
        FakeTagFileLookupQuery query = new();
        query.SetFiles("foo",
            new TagFileLookupRow(IdOf(904), @"C:\notes\a.md", "a.md"),
            new TagFileLookupRow(IdOf(905), @"C:\notes\b.md", "b.md"));
        TagManagementService sut = CreateSut(query, fs);

        TagRewriteResult result = await sut.RenameAsync("foo", "bar", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal([@"C:\notes\a.md"], result.Errors.Keys);
        Assert.Equal(1, result.FilesAffected);
        Assert.Equal("Body mit #foo Tag.", fs.ReadText(@"C:\notes\a.md"));
        Assert.Equal("Mehr #bar und mehr.", fs.ReadText(@"C:\notes\b.md"));
    }

    [Fact]
    public async Task RenameAsync_OnACancelledToken_WritesNothing()
    {
        InMemoryFileSystem fs = new();
        fs.AddFile(@"C:\notes\a.md", "Body mit #foo Tag.");
        FakeTagFileLookupQuery query = new();
        query.SetFiles("foo", new TagFileLookupRow(IdOf(906), @"C:\notes\a.md", "a.md"));
        TagManagementService sut = CreateSut(query, fs);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync().ConfigureAwait(true);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.RenameAsync("foo", "bar", cancelled.Token)).ConfigureAwait(true);

        Assert.Equal("Body mit #foo Tag.", fs.ReadText(@"C:\notes\a.md"));
    }

    private static Guid IdOf(int index) =>
        new(index.ToString("D8", System.Globalization.CultureInfo.InvariantCulture) + "-0000-0000-0000-000000000000");

    private static TagManagementService CreateSut(FakeTagFileLookupQuery query, InMemoryFileSystem fs)
    {
        MarkdownTagRewriter rewriter = new(new TagNormalizer());
        return new TagManagementService(query, fs, rewriter, NullLogger<TagManagementService>.Instance);
    }
}
