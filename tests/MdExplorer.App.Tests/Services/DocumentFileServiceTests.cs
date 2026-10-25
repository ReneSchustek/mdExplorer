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
/// Nach Umbenennen, Verschieben oder Löschen stimmen Datei und Index überein; scheitert der
/// Dateivorgang, bleibt der Index unberührt.
/// </summary>
/// <remarks>
/// Der Kern ist nicht die einzelne Aktion, sondern dass Datei und Index danach dasselbe
/// sagen. Ein Eintrag, der auf einen Pfad zeigt, den es nicht mehr gibt, führt jede Liste
/// ins Leere, und man sucht den Fehler in der Anzeige statt im Vorgang.
/// </remarks>
public sealed class DocumentFileServiceTests
{
    private const string OldPath = @"C:\notes\unter\Alt.md";

    private static readonly Guid FileId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UnknownId = new("99999999-9999-9999-9999-999999999999");

    [Fact]
    public async Task RenameAsync_WithAPlainName_MovesTheFileAndCarriesTheIndexAlong()
    {
        (DocumentFileService sut, FakeFileSystem fs, FakeMarkdownFileRepository repo) = Build();

        DocumentFileResult result = await sut.RenameAsync(FileId, "Neuer Name", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Equal("„Neuer Name“ wurde umbenannt.", result.Message);
        Assert.True(fs.Files.ContainsKey(@"C:\notes\unter\Neuer Name.md"));
        Assert.False(fs.Files.ContainsKey(OldPath));

        MarkdownFile stored = await StoredAsync(repo).ConfigureAwait(true);
        Assert.Equal(@"C:\notes\unter\Neuer Name.md", stored.AbsolutePath);
        Assert.Equal("unter/Neuer Name.md", stored.RelativePath);
        Assert.Equal("Neuer Name", stored.FileNameWithoutExtension);
    }

    [Fact]
    public async Task RenameAsync_WithoutExtension_AddsIt()
    {
        (DocumentFileService sut, FakeFileSystem fs, _) = Build();

        _ = await sut.RenameAsync(FileId, "Ohne Endung", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(fs.Files.ContainsKey(@"C:\notes\unter\Ohne Endung.md"));
    }

    [Fact]
    public async Task RenameAsync_WithExtension_KeepsASingleOne()
    {
        (DocumentFileService sut, FakeFileSystem fs, _) = Build();

        _ = await sut.RenameAsync(FileId, "Mit Endung.md", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(fs.Files.ContainsKey(@"C:\notes\unter\Mit Endung.md"));
        Assert.False(fs.Files.ContainsKey(@"C:\notes\unter\Mit Endung.md.md"));
    }

    /// <remarks>
    /// Der Name kommt aus einem Eingabefeld. Mit Trennzeichen oder Laufwerk darin wäre aus dem
    /// Umbenennen ein Verschieben an jeden beliebigen Ort geworden, auch aus der Index-Wurzel
    /// heraus; <c>Path.Combine</c> übernimmt einen absoluten zweiten Teil ganz.
    /// </remarks>
    [Theory]
    [InlineData(@"..\..\Ausbruch")]
    [InlineData(@"D:\Ausbruch")]
    [InlineData("unter/Ausbruch")]
    [InlineData("..")]
    [InlineData("Na<me")]
    public async Task RenameAsync_WithAPathOrAForbiddenCharacter_IsRefusedAndTouchesNothing(string newName)
    {
        (DocumentFileService sut, FakeFileSystem fs, FakeMarkdownFileRepository repo) = Build();

        DocumentFileResult result = await sut.RenameAsync(FileId, newName, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.Equal("Der neue Name darf weder einen Ordner noch ein in Dateinamen unzulässiges Zeichen enthalten.", result.Message);
        Assert.Equal([OldPath], fs.Files.Keys);
        Assert.Equal(OldPath, (await StoredAsync(repo).ConfigureAwait(true)).AbsolutePath);
    }

    /// <remarks>
    /// Windows unterscheidet Groß- und Kleinschreibung im Dateinamen nicht, zeigt sie aber an.
    /// Wer „alt“ in „Alt“ ändert, will genau das sehen; es ist nicht „dieselbe Datei“.
    /// </remarks>
    [Fact]
    public async Task RenameAsync_ChangingOnlyTheCase_RenamesTheFile()
    {
        (DocumentFileService sut, FakeFileSystem fs, FakeMarkdownFileRepository repo) = Build();

        DocumentFileResult result = await sut.RenameAsync(FileId, "alt", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Equal([@"C:\notes\unter\alt.md"], fs.Files.Keys);
        Assert.Equal("alt", (await StoredAsync(repo).ConfigureAwait(true)).FileNameWithoutExtension);
    }

    [Fact]
    public async Task RenameAsync_OnAnExistingTarget_LeavesBothFileAndIndexUntouched()
    {
        (DocumentFileService sut, FakeFileSystem fs, FakeMarkdownFileRepository repo) = Build();
        fs.Files[@"C:\notes\unter\Belegt.md"] = [1, 2, 3];

        DocumentFileResult result = await sut.RenameAsync(FileId, "Belegt", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.True(fs.Files.ContainsKey(OldPath));
        Assert.Equal(OldPath, (await StoredAsync(repo).ConfigureAwait(true)).AbsolutePath);
    }

    [Fact]
    public async Task RenameAsync_WhenTheFileOperationFails_KeepsTheIndexAndNamesTheCause()
    {
        (DocumentFileService sut, FakeFileSystem fs, FakeMarkdownFileRepository repo) = Build();
        fs.FailOnMove = new UnauthorizedAccessException("Zugriff verweigert");

        DocumentFileResult result = await sut.RenameAsync(FileId, "Neu", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.Equal("Die Datei ließ sich nicht umbenennen: Zugriff verweigert", result.Message);
        Assert.Equal(OldPath, (await StoredAsync(repo).ConfigureAwait(true)).AbsolutePath);
    }

    /// <remarks>
    /// Die Datei steht schon unter dem neuen Namen, nur der Index ließ sich nicht schreiben. Das
    /// ist kein Fehlschlag des Vorgangs: Der Nutzer hat, was er wollte, und der nächste Abgleich
    /// des Indexers holt den Eintrag nach. Eine Ausnahme an dieser Stelle verschwände im Befehl.
    /// </remarks>
    [Fact]
    public async Task RenameAsync_WhenTheIndexCannotBeSaved_ReportsTheRenameAndTheLag()
    {
        (DocumentFileService sut, FakeFileSystem fs, FakeMarkdownFileRepository repo) = Build();
        repo.FailOnSave = new TestDbException("database is locked");

        DocumentFileResult result = await sut.RenameAsync(FileId, "Neu", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Equal(@"C:\notes\unter\Neu.md", result.NewAbsolutePath);
        Assert.Equal("„Neu“ wurde umbenannt. Der Index zieht beim nächsten Abgleich nach.", result.Message);
        Assert.True(fs.Files.ContainsKey(@"C:\notes\unter\Neu.md"));
    }

    [Fact]
    public async Task RenameAsync_WhenTheIndexCannotBeRead_IsRefusedAndTouchesNothing()
    {
        (DocumentFileService sut, FakeFileSystem fs, FakeMarkdownFileRepository repo) = Build();
        repo.FailOnGet = new TestDbException("database is locked");

        DocumentFileResult result = await sut.RenameAsync(FileId, "Neu", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.Equal("Der Index ist gerade nicht erreichbar. Bitte gleich noch einmal versuchen.", result.Message);
        Assert.Equal([OldPath], fs.Files.Keys);
    }

    [Fact]
    public async Task MoveAsync_InsideTheRoot_KeepsTheNameAndUpdatesTheRelativePath()
    {
        (DocumentFileService sut, FakeFileSystem fs, FakeMarkdownFileRepository repo) = Build();

        DocumentFileResult result = await sut.MoveAsync(FileId, @"C:\notes\woanders", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Equal("„Alt“ wurde verschoben.", result.Message);
        Assert.True(fs.Files.ContainsKey(@"C:\notes\woanders\Alt.md"));
        Assert.Equal("woanders/Alt.md", (await StoredAsync(repo).ConfigureAwait(true)).RelativePath);
    }

    [Fact]
    public async Task MoveAsync_OutsideTheIndexRoot_FallsBackToTheBareFileName()
    {
        // Der nächste Indexer-Lauf setzt den Eintrag unter der Wurzel neu an, unter die das
        // Ziel tatsächlich gehört. Bis dahin ist ein Dateiname ehrlicher als ein Pfad, der
        // gegen die falsche Wurzel gerechnet wurde.
        (DocumentFileService sut, _, FakeMarkdownFileRepository repo) = Build();

        _ = await sut.MoveAsync(FileId, @"D:\ganz\woanders", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal("Alt.md", (await StoredAsync(repo).ConfigureAwait(true)).RelativePath);
    }

    /// <remarks>
    /// Ein relativer Ordner bezöge sich auf das Arbeitsverzeichnis des Prozesses, und das ist
    /// nach einem Start über die Verknüpfung irgendein Ordner. Der Ordnerdialog liefert immer
    /// einen vollständigen Pfad; alles andere ist kein Ziel.
    /// </remarks>
    [Fact]
    public async Task MoveAsync_ToARelativeDirectory_IsRefused()
    {
        (DocumentFileService sut, FakeFileSystem fs, _) = Build();

        DocumentFileResult result = await sut.MoveAsync(FileId, @"..\woanders", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.Equal("Das Ziel muss ein vollständiger Ordnerpfad sein.", result.Message);
        Assert.Equal([OldPath], fs.Files.Keys);
    }

    [Fact]
    public async Task MoveAsync_ToTheSamePlace_IsRefusedInsteadOfDone()
    {
        (DocumentFileService sut, _, _) = Build();

        DocumentFileResult result = await sut.MoveAsync(FileId, @"C:\notes\unter", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.Equal("Ziel und Quelle sind dieselbe Datei.", result.Message);
    }

    [Fact]
    public async Task MoveAsync_WhenTheFileOperationFails_NamesTheOperation()
    {
        (DocumentFileService sut, FakeFileSystem fs, _) = Build();
        fs.FailOnMove = new IOException("Datenträger voll");

        DocumentFileResult result = await sut.MoveAsync(FileId, @"C:\notes\woanders", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal("Die Datei ließ sich nicht verschieben: Datenträger voll", result.Message);
    }

    [Fact]
    public async Task DeleteAsync_OnAnIndexedFile_RemovesTheFileAndTheIndexEntry()
    {
        (DocumentFileService sut, FakeFileSystem fs, FakeMarkdownFileRepository repo) = Build();

        DocumentFileResult result = await sut.DeleteAsync(FileId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Equal("„Alt“ wurde gelöscht.", result.Message);
        Assert.Null(result.NewAbsolutePath);
        Assert.Contains(OldPath, fs.DeletedFiles);
        Assert.Null(await repo.GetByIdAsync(FileId, TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task DeleteAsync_WhenTheIndexCannotBeSaved_ReportsTheDeletionAndTheLag()
    {
        (DocumentFileService sut, FakeFileSystem fs, FakeMarkdownFileRepository repo) = Build();
        repo.FailOnSave = new TestDbException("database is locked");

        DocumentFileResult result = await sut.DeleteAsync(FileId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Equal("„Alt“ wurde gelöscht. Der Index zieht beim nächsten Abgleich nach.", result.Message);
        Assert.Contains(OldPath, fs.DeletedFiles);
    }

    [Fact]
    public async Task DeleteAsync_ForAnUnknownDocument_DeletesNothing()
    {
        (DocumentFileService sut, FakeFileSystem fs, _) = Build();

        DocumentFileResult result = await sut.DeleteAsync(UnknownId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.Empty(fs.DeletedFiles);
    }

    /// <remarks>
    /// Die Datei ist in einem anderen Programm offen und lässt sich nicht löschen. Entscheidend
    /// ist hier nicht die Meldung, sondern was nicht passiert: Der Index-Eintrag bleibt
    /// stehen. Ihn zu entfernen, während die Datei noch da ist, hieße sie unauffindbar zu
    /// machen; der Vorgang wäre gescheitert und hätte trotzdem etwas kaputt gemacht.
    /// </remarks>
    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task DeleteAsync_WhenTheFileIsLocked_KeepsTheIndexEntry(Type failureKind)
    {
        (DocumentFileService sut, FakeFileSystem fs, FakeMarkdownFileRepository repo) = Build();
        fs.FailOnDelete = failureKind == typeof(IOException)
            ? new IOException("Die Datei wird von einem anderen Programm verwendet.")
            : new UnauthorizedAccessException("Zugriff verweigert.");

        DocumentFileResult result = await sut.DeleteAsync(FileId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.StartsWith("Die Datei ließ sich nicht löschen: ", result.Message, StringComparison.Ordinal);
        Assert.NotNull(await repo.GetByIdAsync(FileId, TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    /// <remarks>
    /// Alle drei Vorgänge greifen auf denselben Eintrag zu. Ist er zwischen dem Klick und dem
    /// Ausführen verschwunden (der Aufräumdurchgang des Indexers läuft nebenher), muss jeder
    /// von ihnen das sagen, statt auf einem Nullwert weiterzuarbeiten.
    /// </remarks>
    [Fact]
    public async Task RenameMoveDelete_OnAnEntryThatIsGone_SayThatItIsGone()
    {
        (DocumentFileService sut, _, _) = Build();

        DocumentFileResult renamed = await sut.RenameAsync(UnknownId, "Neu", TestContext.Current.CancellationToken).ConfigureAwait(true);
        DocumentFileResult moved = await sut.MoveAsync(UnknownId, @"C:\notes\woanders", TestContext.Current.CancellationToken).ConfigureAwait(true);
        DocumentFileResult removed = await sut.DeleteAsync(UnknownId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        foreach (DocumentFileResult result in (DocumentFileResult[])[renamed, moved, removed])
        {
            Assert.False(result.Succeeded);
            Assert.Equal("Die Datei steht nicht mehr im Index.", result.Message);
        }
    }

    [Fact]
    public async Task GetImpactAsync_WithIncomingLinks_CountsTheLinksThatWouldBreak()
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

        DocumentImpact impact = await sut.GetImpactAsync(UnknownId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Empty(impact.Title);
        Assert.Equal(0, impact.IncomingLinkCount);
    }

    private static async Task<MarkdownFile> StoredAsync(FakeMarkdownFileRepository repository)
    {
        MarkdownFile? stored = await repository.GetByIdAsync(FileId, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.NotNull(stored);
        return stored;
    }

    private static (DocumentFileService Service, FakeFileSystem FileSystem, FakeMarkdownFileRepository Repository) Build(int incomingLinks = 0)
    {
        FakeFileSystem fileSystem = new();
        fileSystem.Files[OldPath] = [1, 2, 3];

        FakeMarkdownFileRepository repository = new();
        repository.Add(new MarkdownFile
        {
            Id = FileId,
            AbsolutePath = OldPath,
            RelativePath = "unter/Alt.md",
            FileNameWithoutExtension = "Alt",
            ContentHash = "hash",
        });

        // Feste Kennungen je Quelle: Die Zahl zählt, nicht die Kennung, und ein Test soll bei
        // jedem Lauf dieselben Daten sehen.
        List<RelatedDocument> incoming =
        [
            .. Enumerable.Range(0, incomingLinks)
                .Select(index => new RelatedDocument(new Guid(index + 1, 0, 0, new byte[8]), $"Quelle{index}", $"Quelle{index}.md"))
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

    /// <summary>Liefert vorgegebene Verbindungen, aus denen die Zahl gefährdeter Links stammt.</summary>
    private sealed class FakeGraphService(DocumentRelations relations) : IGraphService
    {
        public Task<GraphSnapshot> BuildSnapshotAsync(GraphFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult(GraphSnapshot.Empty);

        public Task<DocumentRelations> GetRelationsAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
            Task.FromResult(relations);
    }
}
