using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MdExplorer.Core.Abstractions;
using MdExplorer.Indexer.Abstractions;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Knoten im Ordnerbaum: ein Verzeichnis oder eine Markdown-Datei als Blatt, die beim Anklicken
/// die Vorschau öffnet. Die Kinder eines Verzeichnisses, Ordner wie Dateien, werden erst beim
/// ersten Aufklappen gelesen. Bis dahin steht ein Platzhalterkind darin, sonst zeigte der Baum
/// kein Aufklappzeichen.
/// </summary>
internal sealed partial class TreeNodeViewModel : ObservableObject
{
    private const string MarkdownSearchPattern = "*.md";

    private readonly IFileSystem? _fileSystem;
    private readonly IExclusionFilter? _exclusionFilter;
    private readonly string? _rootAbsolutePath;
    private readonly IReadOnlySet<string> _exclusions;
    private readonly Func<string, bool>? _isUiExcluded;
    private Task? _childrenLoad;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// <see langword="true"/>, wenn dieser Knoten von der Indexierung ausgeschlossen ist, direkt
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

    /// <summary>Erzeugt einen Verzeichnisknoten mit Platzhalterkind für das verzögerte Laden.</summary>
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
        // Der Root bleibt über die ganze Hierarchie konstant, für die IExclusionFilter-API.
        _rootAbsolutePath = rootAbsolutePath ?? absolutePath;
        _exclusions = exclusions;
        _isUiExcluded = isUiExcluded;
        IsDirectlyExcluded = isUiExcluded?.Invoke(absolutePath) ?? false;
        IsExcluded = parentIsExcluded || IsDirectlyExcluded;
        // Jeder Ordner hat seinen eigenen Platzhalter: Ein geteilter trüge Auswahl und
        // Aufklappzustand von einem Baum in den anderen.
        Children = [new TreeNodeViewModel()];
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
        _childrenLoad = Task.CompletedTask;
    }

    /// <summary>Das Platzhalterkind eines noch nicht aufgeklappten Verzeichnisses.</summary>
    private TreeNodeViewModel()
    {
        AbsolutePath = string.Empty;
        DisplayName = "…";
        IsMarkdownFile = false;
        _exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Children = [];
        _childrenLoad = Task.CompletedTask;
    }

    /// <summary>Absoluter Pfad des Verzeichnisses oder der Datei.</summary>
    public string AbsolutePath { get; }

    /// <summary>Anzeigename im Baum.</summary>
    public string DisplayName { get; }

    /// <summary><see langword="true"/>, wenn der Knoten eine Markdown-Datei repräsentiert.</summary>
    public bool IsMarkdownFile { get; }

    /// <summary>Sichtbare Kinder. Verzeichnis: zuerst der Platzhalter, nach dem Aufklappen die echten Einträge. Datei: leer.</summary>
    public ObservableCollection<TreeNodeViewModel> Children { get; }

    /// <summary>
    /// Liest die Kinder beim ersten Aufruf; jeder weitere Aufruf liefert denselben Vorgang.
    /// </summary>
    /// <remarks>
    /// Das Verzeichnis wird im Hintergrund gelesen, die Kinder setzt der Aufrufer-Kontext ein, in
    /// der Anwendung also der UI-Thread. Eine Netzfreigabe, die nicht antwortet, hält damit nicht
    /// die Oberfläche an. Ein Verzeichnis, das sich nicht lesen lässt, zeigt keine Kinder.
    /// </remarks>
    /// <returns>Der Ladevorgang.</returns>
    public Task LoadChildrenAsync() => _childrenLoad ??= LoadChildrenCoreAsync();

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            _ = LoadChildrenAsync();
        }
    }

    private async Task LoadChildrenCoreAsync()
    {
        // Nur Verzeichnisknoten haben ein Dateisystem; Blätter und Platzhalter laden nie.
        if (_fileSystem is not { } fileSystem)
        {
            return;
        }

        ChildListing listing = await Task.Run(() => ReadChildren(fileSystem)).ConfigureAwait(true);

        Children.Clear();
        foreach (string directory in listing.Directories)
        {
            Children.Add(new TreeNodeViewModel(
                directory,
                Path.GetFileName(directory),
                fileSystem,
                _exclusions,
                _exclusionFilter,
                _rootAbsolutePath,
                _isUiExcluded,
                parentIsExcluded: IsExcluded));
        }
        foreach (string file in listing.Files)
        {
            Children.Add(new TreeNodeViewModel(file, Path.GetFileName(file)));
        }
    }

    // Läuft im Hintergrund und fasst nur das Dateisystem an, keine Eigenschaft, an der die Oberfläche hängt.
    private ChildListing ReadChildren(IFileSystem fileSystem)
    {
        if (!fileSystem.DirectoryExists(AbsolutePath))
        {
            return ChildListing.Empty;
        }
        try
        {
            return new ChildListing(VisibleDirectories(fileSystem), VisibleMarkdownFiles(fileSystem));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // „Gerät nicht bereit“, getrenntes Netzlaufwerk, verweigerter Zugriff: Der Ordner
            // bleibt leer, statt dass das Aufklappen eine Ausnahme wirft.
            return ChildListing.Empty;
        }
    }

    // Verzeichnisse fallen nur über die Namensliste heraus. Die Glob-Muster greifen bei den
    // Dateien darunter; ein von Hand pausierter Ordner bleibt sichtbar, nur ausgegraut.
    private string[] VisibleDirectories(IFileSystem fileSystem) =>
    [
        .. fileSystem.EnumerateDirectories(AbsolutePath)
            .Where(directory => Path.GetFileName(directory) is { Length: > 0 } name && !_exclusions.Contains(name))
            .Order(StringComparer.OrdinalIgnoreCase),
    ];

    private string[] VisibleMarkdownFiles(IFileSystem fileSystem) =>
    [
        .. fileSystem.EnumerateFiles(AbsolutePath, MarkdownSearchPattern, recursive: false)
            .Where(file => Path.GetFileName(file).Length > 0 && !IsExcludedFile(file))
            .Order(StringComparer.OrdinalIgnoreCase),
    ];

    private bool IsExcludedFile(string file) =>
        _exclusionFilter is not null
        && _rootAbsolutePath is not null
        && _exclusionFilter.IsExcluded(file, _rootAbsolutePath);

    /// <summary>Was das Lesen eines Verzeichnisses ergeben hat.</summary>
    /// <param name="Directories">Die sichtbaren Unterverzeichnisse, sortiert.</param>
    /// <param name="Files">Die sichtbaren Markdown-Dateien, sortiert.</param>
    private sealed record ChildListing(IReadOnlyList<string> Directories, IReadOnlyList<string> Files)
    {
        public static ChildListing Empty { get; } = new([], []);
    }
}
