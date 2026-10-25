using System.ComponentModel;
using MdExplorer.App.Services;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Öffnet, was in einer der drei Auswahllisten (Ordnerbaum, Alle Dateien, Suche) angeklickt wird,
/// und setzt die Liste zurück, wenn der Nutzer bei ungespeicherten Änderungen abbricht.
/// </summary>
/// <remarks>
/// Ohne das Zurücksetzen stünde in der Liste eine Datei markiert, die gar nicht offen ist. Die
/// Klasse merkt sich deshalb je Liste, was zuletzt gezeigt wurde, und unterscheidet einen
/// Wechsel des Nutzers von ihrem eigenen Zurückspringen.
/// </remarks>
internal sealed class PanelSelectionCoordinator : IDisposable
{
    private readonly FolderTreeViewModel _folderTree;
    private readonly AllFilesViewModel _allFiles;
    private readonly SearchViewModel _search;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Func<Guid, Task<bool>> _openDocument;
    private readonly Func<string, Task<bool>> _openPath;
    private readonly ILogger _logger;

    // Was die drei Listen zuletzt gezeigt haben. Die Listen melden eine angeklickte Datei, bevor
    // sie die neue Auswahl veröffentlichen; bis dahin gilt die vorige als gezeigt.
    private SearchResultItemViewModel? _shownSearchResult;
    private AllFilesItemViewModel? _shownAllFilesItem;
    private TreeNodeViewModel? _shownTreeNode;

    // Wahr, solange eine Liste ihre Auswahl zurücksetzt. Der Wechsel ist dann kein Auftrag zum
    // Laden; ohne den Merker käme beim Zurückspringen dieselbe Frage gleich noch einmal.
    private bool _restoringSelection;

    /// <summary>Verbindet die Listen mit dem Öffnen von Dokumenten.</summary>
    public PanelSelectionCoordinator(
        MainPanels panels,
        IUiDispatcher uiDispatcher,
        Func<Guid, Task<bool>> openDocument,
        Func<string, Task<bool>> openPath,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(openDocument);
        ArgumentNullException.ThrowIfNull(openPath);
        ArgumentNullException.ThrowIfNull(logger);

        _folderTree = panels.FolderTree;
        _allFiles = panels.AllFiles;
        _search = panels.Search;
        _uiDispatcher = uiDispatcher;
        _openDocument = openDocument;
        _openPath = openPath;
        _logger = logger;

        _folderTree.PropertyChanged += OnFolderTreeChanged;
        _folderTree.FileSelected += OnFolderTreeFileSelected;
        _allFiles.FileSelected += OnAllFilesFileSelected;
        _allFiles.PropertyChanged += OnAllFilesChanged;
        _search.PropertyChanged += OnSearchChanged;
    }

    /// <summary>
    /// Markiert in der Trefferliste das geöffnete Dokument, ohne es ein zweites Mal zu laden.
    /// </summary>
    /// <param name="markdownFileId">Das geöffnete Dokument.</param>
    public void MarkOpenSearchResult(Guid? markdownFileId)
    {
        if (markdownFileId is not { } id)
        {
            return;
        }

        SearchResultItemViewModel? matchingItem = _search.Results.FirstOrDefault(item => item.MarkdownFileId == id);
        if (matchingItem is null)
        {
            return;
        }

        RestoreSelection(() => _search.SelectedResult = matchingItem);
        _shownSearchResult = matchingItem;
    }

    /// <summary>Trennt die Abonnements der Listen.</summary>
    public void Dispose()
    {
        _folderTree.PropertyChanged -= OnFolderTreeChanged;
        _folderTree.FileSelected -= OnFolderTreeFileSelected;
        _allFiles.FileSelected -= OnAllFilesFileSelected;
        _allFiles.PropertyChanged -= OnAllFilesChanged;
        _search.PropertyChanged -= OnSearchChanged;
    }

    private void OnFolderTreeChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.Equals(args.PropertyName, nameof(FolderTreeViewModel.SelectedPathPrefix), StringComparison.Ordinal))
        {
            _search.PathPrefixFilter = _folderTree.SelectedPathPrefix;
        }
        else if (string.Equals(args.PropertyName, nameof(FolderTreeViewModel.SelectedNode), StringComparison.Ordinal))
        {
            // Ordner zählen ebenso, weil auch sie markiert bleiben.
            _shownTreeNode = _folderTree.SelectedNode;
        }
    }

    private void OnAllFilesChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.Equals(args.PropertyName, nameof(AllFilesViewModel.SelectedItem), StringComparison.Ordinal))
        {
            _shownAllFilesItem = _allFiles.SelectedItem;
        }
    }

    private void OnSearchChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_restoringSelection
            || !string.Equals(args.PropertyName, nameof(SearchViewModel.SelectedResult), StringComparison.Ordinal))
        {
            return;
        }

        SearchResultItemViewModel? previous = _shownSearchResult;
        SearchResultItemViewModel? selected = _search.SelectedResult;
        _shownSearchResult = selected;
        if (selected is null)
        {
            return;
        }
        OpenOrRestore(
            () => _openDocument(selected.MarkdownFileId),
            () =>
            {
                _search.SelectedResult = previous;
                _shownSearchResult = previous;
            });
    }

    private void OnFolderTreeFileSelected(string absolutePath)
    {
        if (_restoringSelection)
        {
            return;
        }

        TreeNodeViewModel? previous = _shownTreeNode;
        TreeNodeViewModel? selected = _folderTree.SelectedNode;
        OpenOrRestore(
            () => _openPath(absolutePath),
            () =>
            {
                // Die Markierung im Baum hängt an IsSelected der Knoten; der Baum gibt die
                // zurückgesetzte Auswahl dann selbst an SelectedNode weiter.
                selected?.IsSelected = false;
                previous?.IsSelected = true;
                _folderTree.SelectedNode = previous;
            });
    }

    private void OnAllFilesFileSelected(string absolutePath)
    {
        if (_restoringSelection)
        {
            return;
        }

        AllFilesItemViewModel? previous = _shownAllFilesItem;
        OpenOrRestore(
            () => _openPath(absolutePath),
            () => _allFiles.SelectedItem = FindListItem(previous));
    }

    // Die Liste baut ihre Einträge bei jedem Filtern neu auf; gesucht wird deshalb nach dem Pfad,
    // nicht nach dem alten Eintrag.
    private AllFilesItemViewModel? FindListItem(AllFilesItemViewModel? previous) =>
        previous is null
            ? null
            : _allFiles.Items.FirstOrDefault(item => string.Equals(item.AbsolutePath, previous.AbsolutePath, StringComparison.OrdinalIgnoreCase));

    private void OpenOrRestore(Func<Task<bool>> open, Action restore) =>
        ObservedTask.Forget(OpenOrRestoreAsync(open, restore), _logger);

    private async Task OpenOrRestoreAsync(Func<Task<bool>> open, Action restore)
    {
        if (await open().ConfigureAwait(true))
        {
            return;
        }

        // Erst nach dem laufenden Auswahlwechsel zurücksetzen: Während die Liste ihre neue
        // Auswahl überträgt, übernimmt sie einen Wert aus dem ViewModel nicht.
        _uiDispatcher.Post(() => RestoreSelection(restore));
    }

    private void RestoreSelection(Action restore)
    {
        _restoringSelection = true;
        try
        {
            restore();
        }
        finally
        {
            _restoringSelection = false;
        }
    }
}
