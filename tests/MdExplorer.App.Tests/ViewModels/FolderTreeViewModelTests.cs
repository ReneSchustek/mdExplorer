using System.IO;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Der Ordnerbaum zeigt nur vorhandene Wurzeln, lädt Unterordner und Markdown-Dateien erst
/// beim Aufklappen und gibt der Suche einen Pfadfilter relativ zur Wurzel. Pausierte Ordner
/// stehen in den Einstellungen und vererben ihren Ausschluss an die Kinder.
/// </summary>
/// <remarks>
/// Der Baum liest über <c>IFileSystem</c>; die Tests bauen den Bestand im Speicher auf und
/// legen keine Datei auf der Platte an.
/// </remarks>
public sealed class FolderTreeViewModelTests
{
    private const string Root = @"C:\notizen";

    [Fact]
    public void Roots_BuiltFromSettings_OnlyExistingDirectories()
    {
        FakeFileSystem fs = FileSystemWith(Root);

        using FolderTreeViewModel vm = Create(fs, BuildSettings(Root, @"C:\gibt-es-nicht"));

        Assert.Equal(Root, Assert.Single(vm.Roots).AbsolutePath);
    }

    [Fact]
    public async Task OnExpand_LoadsChildrenThroughTheFileSystem_ReplacingThePlaceholder()
    {
        FakeFileSystem fs = FileSystemWith(Root, Root + @"\Sub1", Root + @"\Sub2", Root + @"\.git");
        using FolderTreeViewModel vm = Create(fs, BuildSettings(Root));
        TreeNodeViewModel root = vm.Roots[0];
        _ = Assert.Single(root.Children);

        await root.LoadChildrenAsync().ConfigureAwait(true);

        Assert.Equal(["Sub1", "Sub2"], root.Children.Select(child => child.DisplayName));
    }

    [Fact]
    public async Task Expanding_StartsTheSameLoadThatCanBeAwaited()
    {
        FakeFileSystem fs = FileSystemWith(Root, Root + @"\Sub1");
        using FolderTreeViewModel vm = Create(fs, BuildSettings(Root));
        TreeNodeViewModel root = vm.Roots[0];

        root.IsExpanded = true;
        await root.LoadChildrenAsync().ConfigureAwait(true);

        Assert.Equal("Sub1", Assert.Single(root.Children).DisplayName);
    }

    [Fact]
    public async Task Expanding_ReadsTheDirectoryOffTheCallingThread()
    {
        // Das Aufklappen kommt aus dem Setter von IsExpanded, also vom UI-Thread. Eine
        // Netzfreigabe, die nicht antwortet, hielte sonst die ganze Oberfläche an.
        using ManualResetEventSlim gate = new(initialState: false);
        FakeFileSystem fs = FileSystemWith(Root, Root + @"\Sub1");
        fs.EnumerationGate = gate;
        using FolderTreeViewModel vm = Create(fs, BuildSettings(Root));

        Task load = vm.Roots[0].LoadChildrenAsync();
        bool returnedWhileReading = !load.IsCompleted;
        gate.Set();
        await load.ConfigureAwait(true);

        Assert.True(returnedWhileReading);
        _ = Assert.Single(vm.Roots[0].Children);
    }

    [Fact]
    public void EveryFolder_HasItsOwnPlaceholder()
    {
        // Ein geteilter Platzhalter trüge Auswahl und Aufklappzustand von einem Baum in den
        // anderen.
        FakeFileSystem fs = FileSystemWith(@"C:\a", @"C:\b");
        using FolderTreeViewModel vm = Create(fs, BuildSettings(@"C:\a", @"C:\b"));

        Assert.NotSame(vm.Roots[0].Children[0], vm.Roots[1].Children[0]);
    }

    [Fact]
    public void OnRootSelected_ClearsPathPrefix_ForGlobalSearch()
    {
        // Die Wurzel auszuwählen schränkt die Suche nicht ein. Der Index speichert Pfade
        // relativ zur Wurzel; ein absoluter Wurzelpfad als Präfix griffe nie und ließe die
        // Trefferliste leer. Deshalb ist der Filter hier null.
        FakeFileSystem fs = FileSystemWith(Root);
        using FolderTreeViewModel vm = Create(fs, BuildSettings(Root));

        vm.SelectedNode = vm.Roots[0];

        Assert.Null(vm.SelectedPathPrefix);
    }

    [Fact]
    public async Task OnSubfolderSelected_SetsRelativePathPrefix()
    {
        // Ein Unterordner schränkt die Suche ein; der Filter muss relativ zur Wurzel sein
        // (mit Separator-Endung), damit er gegen die relativen Index-Pfade matcht.
        FakeFileSystem fs = FileSystemWith(Root, Root + @"\Sub1");
        using FolderTreeViewModel vm = Create(fs, BuildSettings(Root));
        TreeNodeViewModel root = vm.Roots[0];
        await root.LoadChildrenAsync().ConfigureAwait(true);

        vm.SelectedNode = Assert.Single(root.Children);

        Assert.Equal("Sub1" + Path.DirectorySeparatorChar, vm.SelectedPathPrefix);
    }

    [Fact]
    public async Task OnExpand_AlsoListsMarkdownFiles_AsLeafNodes()
    {
        FakeFileSystem fs = FileSystemWith(Root, Root + @"\Sub1");
        fs.Files[Root + @"\alpha.md"] = [];
        fs.Files[Root + @"\beta.md"] = [];
        fs.Files[Root + @"\ignored.txt"] = [];
        using FolderTreeViewModel vm = Create(fs, BuildSettings(Root));
        TreeNodeViewModel root = vm.Roots[0];

        await root.LoadChildrenAsync().ConfigureAwait(true);

        Assert.Equal(["Sub1", "alpha.md", "beta.md"], root.Children.Select(child => child.DisplayName));
        Assert.Equal([false, true, true], root.Children.Select(child => child.IsMarkdownFile));
    }

    [Fact]
    public async Task PauseIndexing_OnFolderNode_AppendsAbsolutePathToSettings()
    {
        FakeFileSystem fs = FileSystemWith(Root, Root + @"\PauseMe");
        FakeSettingsService settings = new(BuildSettings(Root));
        using FolderTreeViewModel vm = Create(fs, settings);
        TreeNodeViewModel root = vm.Roots[0];
        await root.LoadChildrenAsync().ConfigureAwait(true);
        TreeNodeViewModel pauseTarget = Assert.Single(root.Children);

        await vm.PauseIndexingCommand.ExecuteAsync(pauseTarget).ConfigureAwait(true);

        _ = Assert.Single(settings.Current.Indexing.UiExcludedFolders, p => string.Equals(p, Root + @"\PauseMe", StringComparison.OrdinalIgnoreCase));
        Assert.True(pauseTarget.IsExcluded);
        Assert.True(pauseTarget.IsDirectlyExcluded);
    }

    [Fact]
    public async Task ResumeIndexing_OnPreviouslyPausedNode_RemovesPathFromSettings()
    {
        const string paused = Root + @"\ResumeMe";
        FakeFileSystem fs = FileSystemWith(Root, paused);
        FakeSettingsService settings = new(BuildSettings([Root], [paused]));
        using FolderTreeViewModel vm = Create(fs, settings);
        TreeNodeViewModel root = vm.Roots[0];
        await root.LoadChildrenAsync().ConfigureAwait(true);
        TreeNodeViewModel resumeTarget = Assert.Single(root.Children);
        Assert.True(resumeTarget.IsDirectlyExcluded);

        await vm.ResumeIndexingCommand.ExecuteAsync(resumeTarget).ConfigureAwait(true);

        Assert.Empty(settings.Current.Indexing.UiExcludedFolders);
        Assert.False(resumeTarget.IsExcluded);
        Assert.False(resumeTarget.IsDirectlyExcluded);
    }

    [Fact]
    public async Task PauseIndexing_OnAlreadyExcludedNode_DoesNotDuplicateEntry()
    {
        const string paused = Root + @"\Already";
        FakeFileSystem fs = FileSystemWith(Root, paused);
        FakeSettingsService settings = new(BuildSettings([Root], [paused]));
        using FolderTreeViewModel vm = Create(fs, settings);
        TreeNodeViewModel root = vm.Roots[0];
        await root.LoadChildrenAsync().ConfigureAwait(true);

        await vm.PauseIndexingCommand.ExecuteAsync(Assert.Single(root.Children)).ConfigureAwait(true);

        _ = Assert.Single(settings.Current.Indexing.UiExcludedFolders);
    }

    [Fact]
    public async Task IsExcluded_PropagatesToChildren_OnInitialBuild()
    {
        const string parentFolder = Root + @"\Parent";
        FakeFileSystem fs = FileSystemWith(Root, parentFolder, parentFolder + @"\Child");
        using FolderTreeViewModel vm = Create(fs, BuildSettings([Root], [parentFolder]));
        TreeNodeViewModel root = vm.Roots[0];
        await root.LoadChildrenAsync().ConfigureAwait(true);
        TreeNodeViewModel parent = Assert.Single(root.Children);
        Assert.True(parent.IsExcluded);
        Assert.True(parent.IsDirectlyExcluded);

        await parent.LoadChildrenAsync().ConfigureAwait(true);
        TreeNodeViewModel child = Assert.Single(parent.Children);

        // Kind erbt IsExcluded vom Eltern, ist aber selbst nicht direkt in der Settings-Liste.
        Assert.True(child.IsExcluded);
        Assert.False(child.IsDirectlyExcluded);
    }

    [Fact]
    public async Task OnFileSelected_FiresFileSelectedEventWithAbsolutePath()
    {
        const string filePath = Root + @"\doc.md";
        FakeFileSystem fs = FileSystemWith(Root);
        fs.Files[filePath] = [];
        using FolderTreeViewModel vm = Create(fs, BuildSettings(Root));
        TreeNodeViewModel root = vm.Roots[0];
        await root.LoadChildrenAsync().ConfigureAwait(true);
        string? raised = null;
        vm.FileSelected += path => raised = path;

        vm.SelectedNode = Assert.Single(root.Children);

        Assert.Equal(filePath, raised);
        // Die Datei liegt direkt unter der Wurzel. Ihr Ordner ist damit die Wurzel selbst,
        // und die Suche bleibt global.
        Assert.Null(vm.SelectedPathPrefix);
    }

    internal static FakeFileSystem FileSystemWith(params string[] directories)
    {
        FakeFileSystem fs = new();
        foreach (string directory in directories)
        {
            _ = fs.Directories.Add(directory);
        }
        return fs;
    }

    internal static AppSettings BuildSettings(params string[] roots) => BuildSettings(roots, []);

    internal static AppSettings BuildSettings(IReadOnlyList<string> roots, IReadOnlyList<string> excluded) => new(
        AppSettings.CurrentSchemaVersion,
        new IndexingSettings(roots, IndexingSettings.DefaultExclusionPatterns, excluded, true),
        AppearanceSettings.Default,
        BehaviorSettings.Default);

    private static FolderTreeViewModel Create(FakeFileSystem fs, AppSettings settings) =>
        Create(fs, new FakeSettingsService(settings));

    private static FolderTreeViewModel Create(FakeFileSystem fs, FakeSettingsService settings) =>
        new(settings, fs, new FakeDialogService(), NullLogger<FolderTreeViewModel>.Instance);
}
