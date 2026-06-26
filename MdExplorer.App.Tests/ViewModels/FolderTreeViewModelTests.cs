using System.IO;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>Tests für die Folder-Tree-Logik (Lazy-Expand, Selected-Prefix).</summary>
public sealed class FolderTreeViewModelTests
{
    [Fact]
    public void Roots_BuiltFromSettings_OnlyExistingDirectories()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "mdexp-tests-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempRoot);
        try
        {
            FakeFileSystem fs = new();
            _ = fs.Directories.Add(tempRoot);
            FakeSettingsService settings = new(BuildSettings(tempRoot, Path.Combine(tempRoot, "does-not-exist-x")));

            using FolderTreeViewModel vm = new(settings, fs);

            _ = Assert.Single(vm.Roots);
            Assert.Equal(tempRoot, vm.Roots[0].AbsolutePath);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void OnExpand_LoadsRealChildren_ReplacingSentinel()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "mdexp-tests-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Path.Combine(tempRoot, "Sub1"));
        _ = Directory.CreateDirectory(Path.Combine(tempRoot, "Sub2"));
        _ = Directory.CreateDirectory(Path.Combine(tempRoot, ".git"));
        try
        {
            FakeFileSystem fs = new();
            _ = fs.Directories.Add(tempRoot);
            FakeSettingsService settings = new(BuildSettings(tempRoot));

            using FolderTreeViewModel vm = new(settings, fs);
            TreeNodeViewModel root = vm.Roots[0];

            _ = Assert.Single(root.Children);
            root.IsExpanded = true;

            Assert.Equal(2, root.Children.Count);
            Assert.DoesNotContain(root.Children, child => string.Equals(child.DisplayName, ".git", StringComparison.Ordinal));
            Assert.Contains(root.Children, child => string.Equals(child.DisplayName, "Sub1", StringComparison.Ordinal));
            Assert.Contains(root.Children, child => string.Equals(child.DisplayName, "Sub2", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void OnRootSelected_ClearsPathPrefix_ForGlobalSearch()
    {
        // Die Wurzel zu selektieren darf die Suche NICHT einschraenken: der Index speichert
        // Pfade relativ zur Wurzel, ein absoluter Wurzel-Prefix wuerde via StartsWith nie
        // greifen und die Trefferliste leer lassen. Deshalb ist der Filter hier null.
        string tempRoot = Path.Combine(Path.GetTempPath(), "mdexp-tests-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempRoot);
        try
        {
            FakeFileSystem fs = new();
            _ = fs.Directories.Add(tempRoot);
            FakeSettingsService settings = new(BuildSettings(tempRoot));

            using FolderTreeViewModel vm = new(settings, fs);
            vm.SelectedNode = vm.Roots[0];

            Assert.Null(vm.SelectedPathPrefix);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void OnSubfolderSelected_SetsRelativePathPrefix()
    {
        // Ein Unterordner schraenkt die Suche ein — der Filter muss relativ zur Wurzel sein
        // (mit Separator-Endung), damit er gegen die relativen Index-Pfade matcht.
        string tempRoot = Path.Combine(Path.GetTempPath(), "mdexp-tests-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Path.Combine(tempRoot, "Sub1"));
        try
        {
            FakeFileSystem fs = new();
            _ = fs.Directories.Add(tempRoot);
            FakeSettingsService settings = new(BuildSettings(tempRoot));

            using FolderTreeViewModel vm = new(settings, fs);
            TreeNodeViewModel root = vm.Roots[0];
            root.IsExpanded = true;
            TreeNodeViewModel sub = Assert.Single(
                root.Children,
                child => string.Equals(child.DisplayName, "Sub1", StringComparison.Ordinal));

            vm.SelectedNode = sub;

            Assert.Equal("Sub1" + Path.DirectorySeparatorChar, vm.SelectedPathPrefix);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void OnExpand_AlsoListsMarkdownFiles_AsLeafNodes()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "mdexp-tests-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempRoot);
        _ = Directory.CreateDirectory(Path.Combine(tempRoot, "Sub1"));
        File.WriteAllText(Path.Combine(tempRoot, "alpha.md"), "# Alpha");
        File.WriteAllText(Path.Combine(tempRoot, "beta.md"), "# Beta");
        File.WriteAllText(Path.Combine(tempRoot, "ignored.txt"), "Nicht .md");
        try
        {
            FakeFileSystem fs = new();
            _ = fs.Directories.Add(tempRoot);
            FakeSettingsService settings = new(BuildSettings(tempRoot));

            using FolderTreeViewModel vm = new(settings, fs);
            TreeNodeViewModel root = vm.Roots[0];
            root.IsExpanded = true;

            Assert.Contains(root.Children, child => child.IsMarkdownFile && string.Equals(child.DisplayName, "alpha.md", StringComparison.Ordinal));
            Assert.Contains(root.Children, child => child.IsMarkdownFile && string.Equals(child.DisplayName, "beta.md", StringComparison.Ordinal));
            Assert.Contains(root.Children, child => !child.IsMarkdownFile && string.Equals(child.DisplayName, "Sub1", StringComparison.Ordinal));
            Assert.DoesNotContain(root.Children, child => string.Equals(child.DisplayName, "ignored.txt", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PauseIndexing_OnFolderNode_AppendsAbsolutePathToSettings()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "mdexp-tests-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempRoot);
        _ = Directory.CreateDirectory(Path.Combine(tempRoot, "PauseMe"));
        try
        {
            FakeFileSystem fs = new();
            _ = fs.Directories.Add(tempRoot);
            FakeSettingsService settings = new(BuildSettings(tempRoot));

            using FolderTreeViewModel vm = new(settings, fs);
            TreeNodeViewModel root = vm.Roots[0];
            root.IsExpanded = true;
            TreeNodeViewModel pauseTarget = Assert.Single(root.Children, c => string.Equals(c.DisplayName, "PauseMe", StringComparison.Ordinal));

            await vm.PauseIndexingCommand.ExecuteAsync(pauseTarget).ConfigureAwait(true);

            string expectedPath = Path.Combine(tempRoot, "PauseMe");
            _ = Assert.Single(settings.Current.Indexing.UiExcludedFolders, p => string.Equals(p, expectedPath, StringComparison.OrdinalIgnoreCase));
            Assert.True(pauseTarget.IsExcluded);
            Assert.True(pauseTarget.IsDirectlyExcluded);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ResumeIndexing_OnPreviouslyPausedNode_RemovesPathFromSettings()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "mdexp-tests-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempRoot);
        string pausedFolder = Path.Combine(tempRoot, "ResumeMe");
        _ = Directory.CreateDirectory(pausedFolder);
        try
        {
            FakeFileSystem fs = new();
            _ = fs.Directories.Add(tempRoot);
            AppSettings initial = new(
                AppSettings.CurrentSchemaVersion,
                new IndexingSettings([tempRoot], IndexingSettings.DefaultExclusionPatterns, [pausedFolder], true),
                AppearanceSettings.Default,
                BehaviorSettings.Default);
            FakeSettingsService settings = new(initial);

            using FolderTreeViewModel vm = new(settings, fs);
            TreeNodeViewModel root = vm.Roots[0];
            root.IsExpanded = true;
            TreeNodeViewModel resumeTarget = Assert.Single(root.Children, c => string.Equals(c.DisplayName, "ResumeMe", StringComparison.Ordinal));
            Assert.True(resumeTarget.IsDirectlyExcluded);

            await vm.ResumeIndexingCommand.ExecuteAsync(resumeTarget).ConfigureAwait(true);

            Assert.Empty(settings.Current.Indexing.UiExcludedFolders);
            Assert.False(resumeTarget.IsExcluded);
            Assert.False(resumeTarget.IsDirectlyExcluded);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PauseIndexing_OnAlreadyExcludedNode_DoesNotDuplicateEntry()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "mdexp-tests-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempRoot);
        string pausedFolder = Path.Combine(tempRoot, "Already");
        _ = Directory.CreateDirectory(pausedFolder);
        try
        {
            FakeFileSystem fs = new();
            _ = fs.Directories.Add(tempRoot);
            AppSettings initial = new(
                AppSettings.CurrentSchemaVersion,
                new IndexingSettings([tempRoot], IndexingSettings.DefaultExclusionPatterns, [pausedFolder], true),
                AppearanceSettings.Default,
                BehaviorSettings.Default);
            FakeSettingsService settings = new(initial);

            using FolderTreeViewModel vm = new(settings, fs);
            TreeNodeViewModel root = vm.Roots[0];
            root.IsExpanded = true;
            TreeNodeViewModel paused = Assert.Single(root.Children, c => string.Equals(c.DisplayName, "Already", StringComparison.Ordinal));

            await vm.PauseIndexingCommand.ExecuteAsync(paused).ConfigureAwait(true);

            _ = Assert.Single(settings.Current.Indexing.UiExcludedFolders);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void IsExcluded_PropagatesToChildren_OnInitialBuild()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "mdexp-tests-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempRoot);
        string parentFolder = Path.Combine(tempRoot, "Parent");
        _ = Directory.CreateDirectory(parentFolder);
        _ = Directory.CreateDirectory(Path.Combine(parentFolder, "Child"));
        try
        {
            FakeFileSystem fs = new();
            _ = fs.Directories.Add(tempRoot);
            _ = fs.Directories.Add(parentFolder);
            AppSettings initial = new(
                AppSettings.CurrentSchemaVersion,
                new IndexingSettings([tempRoot], IndexingSettings.DefaultExclusionPatterns, [parentFolder], true),
                AppearanceSettings.Default,
                BehaviorSettings.Default);
            FakeSettingsService settings = new(initial);

            using FolderTreeViewModel vm = new(settings, fs);
            TreeNodeViewModel root = vm.Roots[0];
            root.IsExpanded = true;
            TreeNodeViewModel parent = Assert.Single(root.Children, c => string.Equals(c.DisplayName, "Parent", StringComparison.Ordinal));
            Assert.True(parent.IsExcluded);
            Assert.True(parent.IsDirectlyExcluded);

            parent.IsExpanded = true;
            TreeNodeViewModel child = Assert.Single(parent.Children, c => string.Equals(c.DisplayName, "Child", StringComparison.Ordinal));

            // Kind erbt IsExcluded vom Eltern, ist aber selbst nicht direkt in der Settings-Liste.
            Assert.True(child.IsExcluded);
            Assert.False(child.IsDirectlyExcluded);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void OnFileSelected_FiresFileSelectedEventWithAbsolutePath()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "mdexp-tests-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempRoot);
        string filePath = Path.Combine(tempRoot, "doc.md");
        File.WriteAllText(filePath, "# Doc");
        try
        {
            FakeFileSystem fs = new();
            _ = fs.Directories.Add(tempRoot);
            FakeSettingsService settings = new(BuildSettings(tempRoot));

            using FolderTreeViewModel vm = new(settings, fs);
            TreeNodeViewModel root = vm.Roots[0];
            root.IsExpanded = true;
            TreeNodeViewModel fileNode = Assert.Single(root.Children, child => child.IsMarkdownFile);

            string? raised = null;
            vm.FileSelected += path => raised = path;
            vm.SelectedNode = fileNode;

            Assert.Equal(filePath, raised);
            // Datei liegt direkt unter der Wurzel → Eltern-Verzeichnis ist die Wurzel selbst
            // → kein Scoping, globale Suche (null).
            Assert.Null(vm.SelectedPathPrefix);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static AppSettings BuildSettings(params string[] roots) => new(
        AppSettings.CurrentSchemaVersion,
        new IndexingSettings(roots, IndexingSettings.DefaultExclusionPatterns, [], true),
        AppearanceSettings.Default,
        BehaviorSettings.Default);

    private sealed class FakeSettingsService(AppSettings initial) : ISettingsService
    {
        public AppSettings Current { get; private set; } = initial ?? throw new ArgumentNullException(nameof(initial));

        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(settings);
            AppSettings previous = Current;
            Current = settings;
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(previous, settings));
            return Task.CompletedTask;
        }
    }
}
