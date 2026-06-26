using System.Collections.ObjectModel;
using System.Data.Common;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Indexer.Abstractions;
using Serilog;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// ViewModel der linken Spalte (Datei-Navigation). Bezieht die Roots aus den
/// persistierten <see cref="MdExplorer.Core.Models.AppSettings"/>; UI-Tree-Ausschlüsse
/// für offensichtliche System-Verzeichnisse bleiben weiterhin hartverdrahtet, weil
/// sie reine Darstellungs-Hygiene sind (kein Indexer-Belang). Markdown-Dateien werden
/// pro Verzeichnis als Blattknoten gelistet und feuern beim Selektieren
/// das <see cref="FileSelected"/>-Event. Die Kontextmenü-Aktionen für
/// <c>Indexierung pausieren / wieder aufnehmen</c> liegen ebenfalls hier.
/// </summary>
internal sealed partial class FolderTreeViewModel : ObservableObject, IDisposable
{
    private static readonly IReadOnlySet<string> _treeUiExclusions = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "node_modules",
        "bin",
        "obj",
        ".vs",
    };

    private readonly IFileSystem _fileSystem;
    private readonly ISettingsService _settings;
    private readonly IExclusionFilter? _exclusionFilter;
    private bool _disposed;

    [ObservableProperty]
    private TreeNodeViewModel? _selectedNode;

    [ObservableProperty]
    private string? _selectedPathPrefix;

    /// <summary>Wird ausgelöst, sobald ein Datei-Knoten selektiert wurde (mit dem absoluten Pfad).</summary>
    public event Action<string>? FileSelected;

    /// <summary>Erzeugt das ViewModel und baut die Root-Knoten auf.</summary>
    public FolderTreeViewModel(
        ISettingsService settings,
        IFileSystem fileSystem,
        IExclusionFilter? exclusionFilter = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(fileSystem);

        _settings = settings;
        _fileSystem = fileSystem;
        _exclusionFilter = exclusionFilter;
        Roots = BuildRoots(settings.Current.Indexing.Roots);
        _settings.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>Wurzel-Knoten des Baums (entspricht den konfigurierten Roots aus den Settings).</summary>
    public ObservableCollection<TreeNodeViewModel> Roots { get; }

    /// <summary>Trennt das Settings-Abonnement.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _settings.SettingsChanged -= OnSettingsChanged;
    }

    /// <summary>
    /// Fügt den Pfad des Knotens zu <c>AppSettings.Indexing.UiExcludedFolders</c> hinzu.
    /// Markdown-Dateiknoten besitzen keinen sinnvollen Ausschluss-Begriff (Datei ≠ Ordner),
    /// daher reagiert das Kommando dort als No-Op.
    /// </summary>
    [RelayCommand]
    private async Task PauseIndexingAsync(TreeNodeViewModel? node)
    {
        if (!CanPauseIndexing(node) || node is null)
        {
            return;
        }
        string normalized = NormalizeFolder(node.AbsolutePath);
        IReadOnlyList<string> updated = AddUnique(_settings.Current.Indexing.UiExcludedFolders, normalized);
        await SaveUiExcludedAsync(updated).ConfigureAwait(true);
    }

    /// <summary>
    /// Entfernt den Pfad des Knotens aus <c>AppSettings.Indexing.UiExcludedFolders</c>,
    /// sofern er dort direkt eingetragen ist. Indirekte Ausschlüsse (vererbt vom Eltern-Knoten)
    /// können nur am Eltern wieder aufgenommen werden.
    /// </summary>
    [RelayCommand]
    private async Task ResumeIndexingAsync(TreeNodeViewModel? node)
    {
        if (node is null || node.IsMarkdownFile || !node.IsDirectlyExcluded)
        {
            return;
        }
        string normalized = NormalizeFolder(node.AbsolutePath);
        IReadOnlyList<string> updated = RemoveFolder(_settings.Current.Indexing.UiExcludedFolders, normalized);
        await SaveUiExcludedAsync(updated).ConfigureAwait(true);
    }

    /// <summary>Pause ist nur fuer Ordnerknoten sinnvoll, die nicht bereits ausgeschlossen sind.</summary>
    private static bool CanPauseIndexing(TreeNodeViewModel? node) =>
        node is not null && !node.IsMarkdownFile && !node.IsExcluded;

    partial void OnSelectedNodeChanged(TreeNodeViewModel? value)
    {
        if (value is null)
        {
            SelectedPathPrefix = null;
            return;
        }
        if (value.IsMarkdownFile)
        {
            // Pfad-Filter folgt dem Eltern-Verzeichnis, damit die Such-Spalte nicht leer wird.
            SelectedPathPrefix = ToRelativePrefix(Path.GetDirectoryName(value.AbsolutePath));
            FileSelected?.Invoke(value.AbsolutePath);
            return;
        }
        SelectedPathPrefix = ToRelativePrefix(value.AbsolutePath);
    }

    /// <summary>
    /// Bildet einen absoluten Knotenpfad auf den <em>indexrelativen</em> Prefix ab, gegen den
    /// <c>SearchViewModel</c> die Treffer filtert. Der Index speichert Pfade relativ zur Wurzel
    /// (<c>Path.GetRelativePath(root, …)</c>), darum darf der Filter nicht der absolute Pfad sein —
    /// sonst greift <c>StartsWith</c> nie und die Trefferliste bleibt leer. Wird die Wurzel selbst
    /// (oder ein Pfad ausserhalb aller Roots) selektiert, liefert die Methode <c>null</c>: dann gilt
    /// kein Filter und die Suche bleibt global. Der angehaengte Separator verhindert, dass ein
    /// Prefix wie <c>dotnet</c> faelschlich auch <c>dotnetfoo\…</c> trifft.
    /// </summary>
    private string? ToRelativePrefix(string? absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
        {
            return null;
        }
        foreach (string rootPath in Roots.Select(static root => root.AbsolutePath))
        {
            if (string.Equals(absolutePath, rootPath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            string relative = Path.GetRelativePath(rootPath, absolutePath);
            if (relative.Length == 0
                || relative.StartsWith("..", StringComparison.Ordinal)
                || Path.IsPathRooted(relative))
            {
                continue;
            }
            return relative + Path.DirectorySeparatorChar;
        }
        return null;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!RootsAreSame(args.Previous.Indexing.Roots, args.Current.Indexing.Roots))
        {
            RebuildRoots(args.Current.Indexing.Roots);
            return;
        }
        // Roots unverändert — nur Exklusions-Flags propagieren, damit der Tree nicht kollabiert.
        foreach (TreeNodeViewModel root in Roots)
        {
            RefreshExclusionState(root, parentIsExcluded: false);
        }
    }

    private void RebuildRoots(IReadOnlyList<string> roots)
    {
        Roots.Clear();
        foreach (TreeNodeViewModel node in BuildRoots(roots))
        {
            Roots.Add(node);
        }
    }

    private void RefreshExclusionState(TreeNodeViewModel node, bool parentIsExcluded)
    {
        if (node.IsMarkdownFile)
        {
            return;
        }
        bool direct = IsFolderUiExcluded(node.AbsolutePath);
        node.IsDirectlyExcluded = direct;
        node.IsExcluded = parentIsExcluded || direct;
        foreach (TreeNodeViewModel child in node.Children)
        {
            RefreshExclusionState(child, node.IsExcluded);
        }
    }

    private ObservableCollection<TreeNodeViewModel> BuildRoots(IEnumerable<string> rootPaths)
    {
        ObservableCollection<TreeNodeViewModel> result = [];
        foreach (string path in rootPaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !_fileSystem.DirectoryExists(path))
            {
                continue;
            }
            string display = string.IsNullOrEmpty(Path.GetFileName(path)) ? path : Path.GetFileName(path);
            result.Add(new TreeNodeViewModel(
                path,
                display,
                _fileSystem,
                _treeUiExclusions,
                _exclusionFilter,
                rootAbsolutePath: path,
                isUiExcluded: IsFolderUiExcluded,
                parentIsExcluded: false));
        }
        return result;
    }

    private bool IsFolderUiExcluded(string absolutePath)
    {
        IReadOnlyList<string> excluded = _settings.Current.Indexing.UiExcludedFolders;
        if (excluded.Count == 0 || string.IsNullOrWhiteSpace(absolutePath))
        {
            return false;
        }
        string normalized = NormalizeFolder(absolutePath);
        foreach (string folder in excluded)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }
            if (string.Equals(NormalizeFolder(folder), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private async Task SaveUiExcludedAsync(IReadOnlyList<string> updated)
    {
        AppSettings current = _settings.Current;
        IndexingSettings indexing = current.Indexing with { UiExcludedFolders = updated };
        AppSettings next = current with { Indexing = indexing };
        try
        {
            await _settings.SaveAsync(next, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbException)
        {
            // UI-Tree bleibt auf Vorstand, Settings-Datei wurde nicht geschrieben.
            Log.ForContext<FolderTreeViewModel>().Warning(ex, "UI-Exclusion-Save fehlgeschlagen.");
        }
    }

    private static IReadOnlyList<string> AddUnique(IReadOnlyList<string> existing, string folder)
    {
        if (existing.Any(item => string.Equals(NormalizeFolder(item), folder, StringComparison.OrdinalIgnoreCase)))
        {
            return existing;
        }
        List<string> result = new(existing.Count + 1);
        result.AddRange(existing);
        result.Add(folder);
        return result;
    }

    private static List<string> RemoveFolder(IReadOnlyList<string> existing, string folder)
    {
        List<string> result = new(existing.Count);
        foreach (string item in existing)
        {
            if (string.Equals(NormalizeFolder(item), folder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            result.Add(item);
        }
        return result;
    }

    private static string NormalizeFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool RootsAreSame(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }
        for (int index = 0; index < a.Count; index++)
        {
            if (!string.Equals(a[index], b[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }
}
