using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using MdExplorer.Core.Abstractions;
using MdExplorer.Indexer.Abstractions;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Knoten im TreeView. Repraesentiert entweder ein Verzeichnis (mit Lazy-Loading der Children
/// per Sentinel-Pattern) oder eine Markdown-Datei (Blattknoten, klickbar — oeffnet die Preview).
/// Datei-Knoten werden erst beim Aufklappen des Eltern-Verzeichnisses materialisiert, weil
/// der Verzeichnis-Lazy-Load das einzige Trigger-Event fuer Datei-Enumeration ist.
/// </summary>
internal sealed partial class TreeNodeViewModel : ObservableObject
{
    private const string MarkdownSearchPattern = "*.md";

    private static readonly TreeNodeViewModel _sentinel = new();
    private static readonly EnumerationOptions _enumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    private readonly IFileSystem? _fileSystem;
    private readonly IExclusionFilter? _exclusionFilter;
    private readonly string? _rootAbsolutePath;
    private readonly IReadOnlySet<string> _exclusions;
    private readonly Func<string, bool>? _isUiExcluded;
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// <see langword="true"/>, wenn dieser Knoten von der Indexierung ausgeschlossen ist — direkt
    /// (Pfad in <c>UiExcludedFolders</c>) oder kaskadiert von einem ausgeschlossenen Eltern-Knoten.
    /// Steuert das visuelle Ausgrauen im Folder-Tree.
    /// </summary>
    [ObservableProperty]
    private bool _isExcluded;

    /// <summary>
    /// <see langword="true"/>, wenn der Pfad dieses Knotens selbst in
    /// <c>AppSettings.Indexing.UiExcludedFolders</c> steht. Steuert die Sichtbarkeit
    /// des "Wieder aufnehmen"-Menüpunkts im Kontextmenü.
    /// </summary>
    [ObservableProperty]
    private bool _isDirectlyExcluded;

    private TreeNodeViewModel()
    {
        AbsolutePath = string.Empty;
        DisplayName = "…";
        IsMarkdownFile = false;
        _exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Children = [];
    }

    /// <summary>Erzeugt einen Verzeichnisknoten mit Sentinel-Child fuer Lazy-Loading.</summary>
    public TreeNodeViewModel(
        string absolutePath,
        string displayName,
        IFileSystem fileSystem,
        IReadOnlySet<string> exclusions,
        IExclusionFilter? exclusionFilter,
        string? rootAbsolutePath,
        Func<string, bool>? isUiExcluded = null,
        bool parentIsExcluded = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(exclusions);

        AbsolutePath = absolutePath;
        DisplayName = displayName;
        IsMarkdownFile = false;
        _fileSystem = fileSystem;
        _exclusionFilter = exclusionFilter;
        // Der Root bleibt ueber die ganze Hierarchie konstant — fuer die IExclusionFilter-API.
        _rootAbsolutePath = rootAbsolutePath ?? absolutePath;
        _exclusions = exclusions;
        _isUiExcluded = isUiExcluded;
        IsDirectlyExcluded = isUiExcluded?.Invoke(absolutePath) ?? false;
        IsExcluded = parentIsExcluded || IsDirectlyExcluded;
        Children = [_sentinel];
    }

    /// <summary>Erzeugt einen Datei-Blattknoten (kein Lazy-Load, keine Kinder).</summary>
    public TreeNodeViewModel(string filePath, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        AbsolutePath = filePath;
        DisplayName = fileName;
        IsMarkdownFile = true;
        _exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Children = [];
        _isLoaded = true;
    }

    /// <summary>Absoluter Pfad des Verzeichnisses oder der Datei.</summary>
    public string AbsolutePath { get; }

    /// <summary>Anzeigename im Baum.</summary>
    public string DisplayName { get; }

    /// <summary><see langword="true"/>, wenn der Knoten eine Markdown-Datei repraesentiert.</summary>
    public bool IsMarkdownFile { get; }

    /// <summary>Sichtbare Kinder. Verzeichnis: initial Sentinel, nach Expand reale Eintraege. Datei: leer.</summary>
    public ObservableCollection<TreeNodeViewModel> Children { get; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value || _isLoaded || _fileSystem is null)
        {
            return;
        }
        _isLoaded = true;
        Children.Clear();
        foreach (TreeNodeViewModel child in EnumerateChildDirectories())
        {
            Children.Add(child);
        }
        foreach (TreeNodeViewModel child in EnumerateChildMarkdownFiles())
        {
            Children.Add(child);
        }
    }

    private IEnumerable<TreeNodeViewModel> EnumerateChildDirectories()
    {
        if (_fileSystem is null || !_fileSystem.DirectoryExists(AbsolutePath))
        {
            yield break;
        }
        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(AbsolutePath, "*", _enumerationOptions);
        }
        catch (DirectoryNotFoundException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (string directory in directories.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(directory);
            if (string.IsNullOrEmpty(name) || _exclusions.Contains(name))
            {
                continue;
            }
            yield return new TreeNodeViewModel(
                directory,
                name,
                _fileSystem,
                _exclusions,
                _exclusionFilter,
                _rootAbsolutePath,
                _isUiExcluded,
                parentIsExcluded: IsExcluded);
        }
    }

    private IEnumerable<TreeNodeViewModel> EnumerateChildMarkdownFiles()
    {
        if (_fileSystem is null || !_fileSystem.DirectoryExists(AbsolutePath))
        {
            yield break;
        }
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(AbsolutePath, MarkdownSearchPattern, _enumerationOptions);
        }
        catch (DirectoryNotFoundException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (string file in files.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(file);
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }
            if (_exclusionFilter is not null
                && _rootAbsolutePath is not null
                && _exclusionFilter.IsExcluded(file, _rootAbsolutePath))
            {
                continue;
            }
            yield return new TreeNodeViewModel(file, name);
        }
    }
}
