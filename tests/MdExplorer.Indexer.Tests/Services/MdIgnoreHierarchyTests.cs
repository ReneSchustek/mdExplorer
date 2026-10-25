using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Settings;
using MdExplorer.Indexer.Services;
using MdExplorer.Indexer.Tests.Fakes;

namespace MdExplorer.Indexer.Tests.Services;

/// <summary>
/// <see cref="MdIgnoreHierarchy"/> sammelt die Muster aller <c>.mdignore</c>-Dateien von der Wurzel
/// bis zum Ordner in dieser Reihenfolge, liest jede Datei nur einmal und erst nach
/// <c>Clear</c> erneut. Kehrte sich die Reihenfolge um, überstimmte die Wurzel jede Ausnahme in
/// einem Unterordner.
/// </summary>
public sealed class MdIgnoreHierarchyTests
{
    [Fact]
    public void GetSourcesFor_OnDirectoryNotUnderRoot_ReturnsEmpty()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(@"C:\Wurzel");
        MdIgnoreHierarchy sut = NewHierarchy(fs);

        IReadOnlyList<string> result = Flatten(sut.GetSourcesFor(@"C:\Wurzel", @"C:\Andere\Pfad"));

        Assert.Empty(result);
    }

    [Fact]
    public void GetSourcesFor_OnRootIdenticalToDirectory_ReturnsOnlyRootPatterns()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(@"C:\Wurzel");
        fs.AddFile(@"C:\Wurzel\.mdignore", "**/temp/**\n", DateTime.UnixEpoch);
        MdIgnoreHierarchy sut = NewHierarchy(fs);

        IReadOnlyList<string> result = Flatten(sut.GetSourcesFor(@"C:\Wurzel", @"C:\Wurzel"));

        Assert.Equal(["**/temp/**"], result);
    }

    [Fact]
    public void GetSourcesFor_OnDeepDirectory_ReturnsParentChainInOrder()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(@"C:\Wurzel");
        fs.AddDirectory(@"C:\Wurzel\sub");
        fs.AddDirectory(@"C:\Wurzel\sub\leaf");
        fs.AddFile(@"C:\Wurzel\.mdignore", "root-pattern\n", DateTime.UnixEpoch);
        fs.AddFile(@"C:\Wurzel\sub\.mdignore", "sub-pattern\n", DateTime.UnixEpoch);
        fs.AddFile(@"C:\Wurzel\sub\leaf\.mdignore", "leaf-pattern\n", DateTime.UnixEpoch);
        MdIgnoreHierarchy sut = NewHierarchy(fs);

        IReadOnlyList<string> result = Flatten(sut.GetSourcesFor(@"C:\Wurzel", @"C:\Wurzel\sub\leaf"));

        Assert.Equal(["root-pattern", "sub-pattern", "leaf-pattern"], result);
    }

    [Fact]
    public void GetSourcesFor_NamesTheFolderOfEachMdIgnore_AndSkipsFoldersWithoutOne()
    {
        // Ein Muster gilt relativ zu seinem Ordner. Fehlte der Ordner, ließe sich "*.md" aus
        // sub/.mdignore nicht von demselben Muster an der Wurzel unterscheiden.
        FakeFileSystem fs = new();
        fs.AddDirectory(@"C:\Wurzel");
        fs.AddDirectory(@"C:\Wurzel\sub");
        fs.AddDirectory(@"C:\Wurzel\sub\leaf");
        fs.AddFile(@"C:\Wurzel\sub\.mdignore", "*.md\n", DateTime.UnixEpoch);
        MdIgnoreHierarchy sut = NewHierarchy(fs);

        MdIgnoreSource source = Assert.Single(sut.GetSourcesFor(@"C:\Wurzel", @"C:\Wurzel\sub\leaf"));

        Assert.Equal(@"C:\Wurzel\sub", source.Directory, StringComparer.Ordinal);
        Assert.Equal(["*.md"], source.Patterns);
    }

    [Fact]
    public void GetSourcesFor_OnRepeatedCall_DoesNotReReadFromDisk()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(@"C:\Wurzel");
        fs.AddFile(@"C:\Wurzel\.mdignore", "**/temp/**\n", DateTime.UnixEpoch);
        MdIgnoreHierarchy sut = NewHierarchy(fs);

        _ = sut.GetSourcesFor(@"C:\Wurzel", @"C:\Wurzel");
        _ = sut.GetSourcesFor(@"C:\Wurzel", @"C:\Wurzel");

        Assert.Equal(1, fs.ReadAllBytesCalls);
    }

    [Fact]
    public void Clear_RemovesCachedPatterns_NextCallRereads()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(@"C:\Wurzel");
        fs.AddFile(@"C:\Wurzel\.mdignore", "**/temp/**\n", DateTime.UnixEpoch);
        MdIgnoreHierarchy sut = NewHierarchy(fs);

        _ = sut.GetSourcesFor(@"C:\Wurzel", @"C:\Wurzel");
        sut.Clear();
        _ = sut.GetSourcesFor(@"C:\Wurzel", @"C:\Wurzel");

        Assert.Equal(2, fs.ReadAllBytesCalls);
    }

    [Fact]
    public void GetSourcesFor_OnRelativePath_ResolvesToFullPath()
    {
        // Ein relativer Pfad wird gegen das Arbeitsverzeichnis aufgelöst, und die Quelle trägt den
        // vollen Ordner; ein Muster bezöge sich sonst auf einen Ordner, den es nicht gibt.
        const string relativeRoot = "WurzelRelativ";
        string fullRoot = Path.GetFullPath(relativeRoot);
        FakeFileSystem fs = new();
        fs.AddDirectory(fullRoot);
        fs.AddFile(Path.Combine(fullRoot, ".mdignore"), "root-pattern\n", DateTime.UnixEpoch);
        MdIgnoreHierarchy sut = NewHierarchy(fs);

        MdIgnoreSource source = Assert.Single(sut.GetSourcesFor(relativeRoot, relativeRoot));

        Assert.Equal(fullRoot, source.Directory, StringComparer.Ordinal);
        Assert.Equal(["root-pattern"], source.Patterns);
    }

    private static MdIgnoreHierarchy NewHierarchy(IFileSystem fileSystem) =>
        new(new MdIgnoreReader(fileSystem));

    private static List<string> Flatten(IReadOnlyList<MdIgnoreSource> sources) =>
        [.. sources.SelectMany(source => source.Patterns)];
}
