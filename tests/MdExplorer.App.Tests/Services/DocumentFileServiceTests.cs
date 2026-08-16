using System.IO;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Graph.Abstractions;
using MdExplorer.Graph.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Tests für Umbenennen, Verschieben und Löschen eines Dokuments.
/// </summary>
/// <remarks>
/// Der Kern ist nicht die einzelne Aktion, sondern dass Datei und Index danach dasselbe
/// sagen. Ein Eintrag, der auf einen Pfad zeigt, den es nicht mehr gibt, führt jede Liste
/// ins Leere — und man sucht den Fehler in der Anzeige statt im Vorgang.
/// </remarks>
public sealed class DocumentFileServiceTests
{
    private static readonly Guid FileId = new("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task RenameAsync_MovesTheFileAndCarriesTheIndexAlong()
    {
        (DocumentFileService sut, FakeFileSystem fs, FakeMarkdownFileRepository repo) = Build();

        DocumentFileResult result = await sut.RenameAsync(FileId, "Neuer Name", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.True(fs.Files.ContainsKey(@"C:\notes\unter\Neuer Name.md"));
        Assert.False(fs.Files.ContainsKey(@"C:\notes\unter\Alt.md"));

        MarkdownFile? stored = await repo.GetByIdAsync(FileId, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.NotNull(stored);
        Assert.Equal(@"C:\notes\unter\Neuer Name.md", stored.AbsolutePath);
        Assert.Equal("unter/Neuer Name.md", stored.RelativePath);
        Assert.Equal("Neuer Name", stored.FileNameWithoutExtension);
    }

    [Fact]
    public async Task RenameAsync_AddsTheExtensionWhenTheUserOmitsIt()
    {
        (DocumentFileService sut, FakeFileSystem fs, _) = Build();

        _ = await sut.RenameAsync(FileId, "Ohne Endung", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(fs.Files.ContainsKey(@"C:\notes\unter\Ohne Endung.md"));
    }

    [Fact]
    public async Task RenameAsync_KeepsASingleExtensionWhenTheUserTypesIt()
    {
        (DocumentFileService sut, FakeFileSystem fs, _) = Build();

        _ = await sut.RenameAsync(FileId, "Mit Endung.md", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(fs.Files.ContainsKey(@"C:\notes\unter\Mit Endung.md"));
        Assert.False(fs.Files.ContainsKey(@"C:\notes\unter\Mit Endung.md.md"));
    }

    [Fact]
    public async Task RenameAsync_OnAnExistingTarget_LeavesBothFileAndIndexUntouched()
    {
        (DocumentFileService sut, FakeFileSystem fs, FakeMarkdownFileRepository repo) = Build();
        fs.Files[@"C:\notes\unter\Belegt.md"] = [1, 2, 3];

        DocumentFileResult result = await sut.RenameAsync(FileId, "Belegt", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.True(fs.Files.ContainsKey(@"C:\notes\unter\Alt.md"));
        MarkdownFile? stored = await repo.GetByIdAsync(FileId, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(@"C:\notes\unter\Alt.md", stored!.AbsolutePath);
    }

    [Fact]
    public async Task MoveAsync_KeepsTheNameAndUpdatesTheRelativePath()
    {
        (DocumentFileService sut, FakeFileSystem fs, FakeMarkdownFileRepository repo) = Build();

        DocumentFileResult result = await sut.MoveAsync(FileId, @"C:\notes\woanders", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.True(fs.Files.ContainsKey(@"C:\notes\woanders\Alt.md"));
        MarkdownFile? stored = await repo.GetByIdAsync(FileId, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal("woanders/Alt.md", stored!.RelativePath);
    }

    [Fact]
    public async Task MoveAsync_OutsideTheIndexRoot_FallsBackToTheBareFileName()
    {
        // Der nächste Indexer-Lauf setzt den Eintrag unter der Wurzel neu an, unter die das
        // Ziel tatsächlich gehört. Bis dahin ist ein Dateiname ehrlicher als ein Pfad, der
        // gegen die falsche Wurzel gerechnet wurde.
        (DocumentFileService sut, _, FakeMarkdownFileRepository repo) = Build();

        _ = await sut.MoveAsync(FileId, @"D:\ganz\woanders", TestContext.Current.CancellationToken).ConfigureAwait(true);

        MarkdownFile? stored = await repo.GetByIdAsync(FileId, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal("Alt.md", stored!.RelativePath);
    }

    [Fact]
    public async Task MoveAsync_ToTheSamePlace_IsRefusedInsteadOfDone()
    {
        (DocumentFileService sut, _, _) = Build();

        DocumentFileResult result = await sut.MoveAsync(FileId, @"C:\notes\unter", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheFileAndTheIndexEntry()
    {
        (DocumentFileService sut, FakeFileSystem fs, FakeMarkdownFileRepository repo) = Build();

        DocumentFileResult result = await sut.DeleteAsync(FileId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Null(result.NewAbsolutePath);
        Assert.Contains(@"C:\notes\unter\Alt.md", fs.DeletedFiles);
        Assert.Null(await repo.GetByIdAsync(FileId, TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task WhenTheFileOperationFails_TheIndexIsNotTouched()
    {
        (DocumentFileService sut, FakeFileSystem fs, FakeMarkdownFileRepository repo) = Build();
        fs.FailOnMove = new UnauthorizedAccessException("Zugriff verweigert");

        DocumentFileResult result = await sut.RenameAsync(FileId, "Neu", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.Contains("Zugriff verweigert", result.Message, StringComparison.Ordinal);
        MarkdownFile? stored = await repo.GetByIdAsync(FileId, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(@"C:\notes\unter\Alt.md", stored!.AbsolutePath);
    }

    [Fact]
    public async Task ForAnUnknownDocument_NothingHappens()
    {
        (DocumentFileService sut, FakeFileSystem fs, _) = Build();

        DocumentFileResult result = await sut.DeleteAsync(Guid.NewGuid(), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.Empty(fs.DeletedFiles);
    }

    [Fact]
    public async Task GetImpactAsync_CountsTheLinksThatWouldBreak()
    {
        (DocumentFileService sut, _, _) = Build(incomingLinks: 3);

        DocumentImpact impact = await sut.GetImpactAsync(FileId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal("Alt", impact.Title);
        Assert.Equal(3, impact.IncomingLinkCount);
    }

    [Fact]
    public async Task GetImpactAsync_ForAnUnknownDocument_ReportsNothing()
    {
        (DocumentFileService sut, _, _) = Build();

        DocumentImpact impact = await sut.GetImpactAsync(Guid.NewGuid(), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Empty(impact.Title);
        Assert.Equal(0, impact.IncomingLinkCount);
    }

    private static (DocumentFileService Service, FakeFileSystem FileSystem, FakeMarkdownFileRepository Repository) Build(int incomingLinks = 0)
    {
        FakeFileSystem fileSystem = new();
        fileSystem.Files[@"C:\notes\unter\Alt.md"] = [1, 2, 3];

        FakeMarkdownFileRepository repository = new();
        repository.Add(new MarkdownFile
        {
            Id = FileId,
            AbsolutePath = @"C:\notes\unter\Alt.md",
            RelativePath = "unter/Alt.md",
            FileNameWithoutExtension = "Alt",
            ContentHash = "hash",
        });

        List<RelatedDocument> incoming =
        [
            .. Enumerable.Range(0, incomingLinks)
                .Select(index => new RelatedDocument(Guid.NewGuid(), $"Quelle{index}", $"Quelle{index}.md"))
        ];

        ServiceCollection services = new();
        _ = services.AddScoped<IMarkdownFileRepository>(_ => repository);
        _ = services.AddScoped<IGraphService>(_ => new FakeGraphService(new DocumentRelations([], incoming)));
        ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        DocumentFileService service = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            fileSystem,
            NullLogger<DocumentFileService>.Instance);

        return (service, fileSystem, repository);
    }

    private sealed class FakeGraphService(DocumentRelations relations) : IGraphService
    {
        public Task<GraphSnapshot> BuildSnapshotAsync(GraphFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult(GraphSnapshot.Empty);

        public Task<DocumentRelations> GetRelationsAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
            Task.FromResult(relations);
    }
    /// <remarks>
    /// Die Datei ist in einem anderen Programm offen und lässt sich nicht löschen. Entscheidend
    /// ist hier nicht die Meldung, sondern was **nicht** passiert: Der Index-Eintrag bleibt
    /// stehen. Ihn zu entfernen, während die Datei noch da ist, hieße sie unauffindbar zu
    /// machen — der Vorgang wäre gescheitert und hätte trotzdem etwas kaputt gemacht.
    /// </remarks>
    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task DeleteAsync_WhenTheFileIsLocked_KeepsTheIndexEntry(Type fehlerart)
    {
        (DocumentFileService sut, FakeFileSystem fs, FakeMarkdownFileRepository repo) = Build();
        fs.FailOnDelete = fehlerart == typeof(IOException)
            ? new IOException("Die Datei wird von einem anderen Programm verwendet.")
            : new UnauthorizedAccessException("Zugriff verweigert.");

        DocumentFileResult result = await sut.DeleteAsync(FileId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.Contains("nicht löschen", result.Message, StringComparison.Ordinal);
        Assert.NotNull(await repo.GetByIdAsync(FileId, TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    /// <remarks>
    /// Alle drei Vorgänge greifen auf denselben Eintrag zu. Ist er zwischen dem Klick und dem
    /// Ausführen verschwunden — der Aufräumdurchgang des Indexers läuft nebenher —, muss jeder
    /// von ihnen das sagen, statt auf einem Nullwert weiterzuarbeiten.
    /// </remarks>
    [Fact]
    public async Task EveryOperation_OnAnEntryThatIsGone_SaysSo()
    {
        (DocumentFileService sut, _, _) = Build();
        Guid unbekannt = new("99999999-9999-9999-9999-999999999999");

        DocumentFileResult umbenannt = await sut.RenameAsync(unbekannt, "Neu", TestContext.Current.CancellationToken).ConfigureAwait(true);
        DocumentFileResult verschoben = await sut.MoveAsync(unbekannt, @"C:\notes\woanders", TestContext.Current.CancellationToken).ConfigureAwait(true);
        DocumentFileResult entfernt = await sut.DeleteAsync(unbekannt, TestContext.Current.CancellationToken).ConfigureAwait(true);

        foreach (DocumentFileResult ergebnis in (DocumentFileResult[])[umbenannt, verschoben, entfernt])
        {
            Assert.False(ergebnis.Succeeded);
            Assert.Contains("nicht mehr im Index", ergebnis.Message, StringComparison.Ordinal);
        }
    }
}
