using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Hosting;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Container-ViewModel der rechten Spalte. Schaltet zwischen <see cref="DocumentPanelMode.Read"/>
/// (WebView2-Vorschau) und <see cref="DocumentPanelMode.Edit"/> (Markdown-Editor) um. Nach dem
/// Speichern rendert es den gespeicherten Text selbst in die Vorschau, statt auf den Indexer zu
/// warten, der die Datei erst nach der Meldung des Datei-Watchers neu liest.
/// </summary>
internal sealed partial class DocumentPanelViewModel : ObservableObject, IDisposable
{
    private readonly IMarkdownParser _markdownParser;
    private readonly IDocumentLocator _documentLocator;
    private readonly UnindexedFileReader _unindexedFiles;
    private readonly IUnsavedChangesGuard _unsavedChanges;
    private readonly ILogger<DocumentPanelViewModel> _logger;
    private readonly LatestOperation _loads = new();
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadMode))]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    private DocumentPanelMode _mode = DocumentPanelMode.Read;

    /// <summary>Erstellt das ViewModel und verdrahtet die Save-Event-Brücke zur Preview.</summary>
    public DocumentPanelViewModel(
        PreviewViewModel preview,
        MarkdownEditorViewModel editor,
        DocumentRelationsViewModel relations,
        IMarkdownParser markdownParser,
        IDocumentLocator documentLocator,
        IFileSystem fileSystem,
        IUnsavedChangesGuard unsavedChanges,
        ILogger<DocumentPanelViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(relations);
        ArgumentNullException.ThrowIfNull(markdownParser);
        ArgumentNullException.ThrowIfNull(documentLocator);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(unsavedChanges);
        ArgumentNullException.ThrowIfNull(logger);

        Preview = preview;
        Editor = editor;
        Relations = relations;
        _markdownParser = markdownParser;
        _documentLocator = documentLocator;
        _unindexedFiles = new UnindexedFileReader(fileSystem, markdownParser, logger);
        _unsavedChanges = unsavedChanges;
        _logger = logger;

        Editor.Saved += OnEditorSaved;
        Relations.OpenRequested += OnRelatedDocumentRequested;
        Relations.FolderRequested += OnFolderRequested;
        Relations.TagRequested += OnTagRequested;
        Relations.DocumentChanged += OnDocumentChanged;
    }

    /// <summary>Ein Klick auf ein verwandtes Dokument; es soll geöffnet werden.</summary>
    public event Action<Guid>? RelatedDocumentRequested;

    /// <summary>Der Ordner des Dokuments soll gezeigt werden (relativ zur Wurzel).</summary>
    public event Action<string>? FolderRequested;

    /// <summary>Eine Kennzeichnung des Dokuments soll gezeigt werden.</summary>
    public event Action<string>? TagRequested;

    /// <summary>
    /// Ein Vorgang hat die Datei verändert: neuer Pfad nach Umbenennen oder Verschieben,
    /// <see langword="null"/> nach dem Löschen.
    /// </summary>
    public event Action<string?>? DocumentChanged;

    /// <summary>Liefert die Preview im Read-Modus.</summary>
    public PreviewViewModel Preview { get; }

    /// <summary>Liefert den Editor im Edit-Modus.</summary>
    public MarkdownEditorViewModel Editor { get; }

    /// <summary>Zeigt, was am geöffneten Dokument hängt.</summary>
    public DocumentRelationsViewModel Relations { get; }

    /// <summary>Wahr, solange das Panel im Lese-Modus ist (Binding-Helfer für Visibility).</summary>
    public bool IsReadMode => Mode == DocumentPanelMode.Read;

    /// <summary>Wahr, solange das Panel im Bearbeiten-Modus ist (Binding-Helfer für Visibility).</summary>
    /// <remarks>
    /// Setzbar, damit der Schalter „Bearbeiten“ in beide Richtungen bindet. Löste er nur den
    /// Befehl aus, wechselte er über die Bedienungshilfen allein seinen Haken, nicht den Modus.
    /// </remarks>
    public bool IsEditMode
    {
        get => Mode == DocumentPanelMode.Edit;
        set => Mode = value ? DocumentPanelMode.Edit : DocumentPanelMode.Read;
    }

    /// <summary>Wahr, solange der Editor Änderungen hält, die nicht auf der Platte stehen.</summary>
    public bool HasUnsavedChanges => _unsavedChanges.HasUnsavedChanges;

    /// <summary>Kennung des angezeigten Dokuments; <see langword="null"/>, wenn keines indiziert offen ist.</summary>
    public Guid? OpenDocumentId => Preview.CurrentDocumentId;

    /// <summary>Leert Vorschau, Editor und Bezüge; danach ist kein Dokument offen.</summary>
    public void Clear()
    {
        Preview.Clear();
        Editor.Clear();
        Relations.Clear();
    }

    /// <summary>Fragt bei ungespeicherten Änderungen nach, bevor das Dokument verlassen wird.</summary>
    /// <param name="cancellationToken">Abbruch des Speicherns.</param>
    /// <returns><see langword="true"/>, wenn das Dokument verlassen werden darf.</returns>
    public Task<bool> ConfirmLeaveAsync(CancellationToken cancellationToken) =>
        _unsavedChanges.ConfirmLeaveAsync(cancellationToken);

    /// <summary>
    /// Lädt das Dokument in Vorschau, Editor und Bezüge. Den Dateipfad für den Editor löst der
    /// <see cref="IDocumentLocator"/> auf, damit der Aufrufer nur die Id kennen muss.
    /// </summary>
    /// <returns>
    /// <see langword="false"/>, wenn der Nutzer bei ungespeicherten Änderungen abgebrochen hat;
    /// dann bleibt das bisherige Dokument im Bereich.
    /// </returns>
    public async Task<bool> LoadAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        if (!await _unsavedChanges.ConfirmLeaveAsync(cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        using LatestOperation.Ticket load = _loads.Begin(cancellationToken);
        await LoadIndexedAsync(markdownFileId, load).ConfigureAwait(true);
        return true;
    }

    /// <summary>
    /// Lädt eine Datei über ihren Pfad. Steht sie schon im Index, geht es über die Id weiter,
    /// damit auch die Bezüge geladen werden. Sonst wird sie direkt vom Datenträger gelesen und
    /// gerendert; das betrifft Dateien, die der Indexer noch nicht erfasst hat.
    /// </summary>
    /// <returns><see langword="false"/>, wenn der Nutzer bei ungespeicherten Änderungen abgebrochen hat.</returns>
    public async Task<bool> LoadByPathAsync(string absolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        // Vor dem Nachschlagen fragen: Sonst entschiede die Antwort erst, wenn das Ziel schon
        // halb geladen ist.
        if (!await _unsavedChanges.ConfirmLeaveAsync(cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        using LatestOperation.Ticket load = _loads.Begin(cancellationToken);
        PathLookup lookup = await LookUpAsync(absolutePath, load).ConfigureAwait(true);
        if (lookup.IndexedId is { } id)
        {
            await LoadIndexedAsync(id, load).ConfigureAwait(true);
        }
        else if (lookup.Succeeded && load.IsCurrent)
        {
            await LoadDirectFromFileAsync(absolutePath, load).ConfigureAwait(true);
        }
        return true;
    }

    /// <summary>Wechselt zwischen Lese- und Bearbeiten-Modus (Schaltfläche oder Strg+E).</summary>
    [RelayCommand]
    public void ToggleMode()
    {
        Mode = Mode == DocumentPanelMode.Read ? DocumentPanelMode.Edit : DocumentPanelMode.Read;
    }

    /// <summary>Speichert den Editor. Das <c>DocumentPanel</c> ruft es über Strg+S.</summary>
    [RelayCommand]
    public Task SaveAsync(CancellationToken cancellationToken) => Editor.SaveAsync(cancellationToken);

    /// <summary>Löst Event-Handler und gibt Ressourcen frei.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _loads.Dispose();
        Editor.Saved -= OnEditorSaved;
        Relations.OpenRequested -= OnRelatedDocumentRequested;
        Relations.FolderRequested -= OnFolderRequested;
        Relations.TagRequested -= OnTagRequested;
        Relations.DocumentChanged -= OnDocumentChanged;
    }

    [LoggerMessage(EventId = 510, Level = LogLevel.Warning, Message = "Editor-Load für {MarkdownFileId} ohne Datei-Pfad — Indexer-Eintrag fehlt.")]
    private static partial void LogPathMissing(ILogger logger, Guid markdownFileId);

    [LoggerMessage(EventId = 511, Level = LogLevel.Error, Message = "Preview-Refresh nach Save fehlgeschlagen.")]
    private static partial void LogPreviewRefreshFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 512, Level = LogLevel.Information, Message = "Datei {Path} direkt aus dem Dateisystem geladen (Indexer noch nicht durch).")]
    private static partial void LogDirectLoadCompleted(ILogger logger, string path);

    [LoggerMessage(EventId = 515, Level = LogLevel.Warning, Message = "Laden von Dokument {MarkdownFileId} fehlgeschlagen; der Bereich bleibt leer.")]
    private static partial void LogLoadFailed(ILogger logger, Guid markdownFileId, Exception exception);

    [LoggerMessage(EventId = 516, Level = LogLevel.Warning, Message = "Nachschlagen von {Path} im Index fehlgeschlagen; der Bereich bleibt leer.")]
    private static partial void LogPathLookupFailed(ILogger logger, string path, Exception exception);

    /// <summary>
    /// Lädt ein indiziertes Dokument in Vorschau, Editor und Bezüge.
    /// </summary>
    /// <remarks>
    /// Zuerst der Pfad, dann die Teile: Scheitert das Nachschlagen, ist noch nichts ausgetauscht.
    /// Scheitert ein Teil danach, wird der ganze Bereich geleert. Vorschau und Editor zeigen so
    /// immer dieselbe Datei oder beide keine; sonst bearbeitete der Nutzer die vorige Datei,
    /// während die Vorschau schon die neue zeigt. Nach jedem Warten zählt nur der jüngste Klick.
    /// </remarks>
    private async Task LoadIndexedAsync(Guid markdownFileId, LatestOperation.Ticket load)
    {
        try
        {
            string? absolutePath = await _documentLocator.GetAbsolutePathAsync(markdownFileId, load.Token).ConfigureAwait(true);
            if (absolutePath is not null && load.IsCurrent)
            {
                await LoadPartsAsync(markdownFileId, absolutePath, load).ConfigureAwait(true);
            }
            else if (load.IsCurrent)
            {
                LogPathMissing(_logger, markdownFileId);
                Clear();
            }
        }
        catch (OperationCanceledException)
        {
            // Ein neuerer Klick hat übernommen; er zeigt sein eigenes Dokument.
        }
        catch (Exception exception) when (DataLoadFailure.IsExpected(exception) || exception is IOException or UnauthorizedAccessException)
        {
            LogLoadFailed(_logger, markdownFileId, exception);
            if (load.IsCurrent)
            {
                Clear();
            }
        }
    }

    private async Task LoadPartsAsync(Guid markdownFileId, string absolutePath, LatestOperation.Ticket load)
    {
        await Preview.LoadAsync(markdownFileId, load.Token).ConfigureAwait(true);
        await Editor.LoadAsync(markdownFileId, absolutePath, load.Token).ConfigureAwait(true);
        // Erst nach dem Editor: Die Kennzeichnungen stehen im Text, nicht im Graphen.
        await Relations.LoadAsync(markdownFileId, [.. Editor.Tags], load.Token).ConfigureAwait(true);
    }

    /// <summary>Schlägt nach, ob die Datei im Index steht; scheitert das, ist der Bereich danach leer.</summary>
    private async Task<PathLookup> LookUpAsync(string absolutePath, LatestOperation.Ticket load)
    {
        try
        {
            Guid? id = await _documentLocator.FindByAbsolutePathAsync(absolutePath, load.Token).ConfigureAwait(true);
            return new PathLookup(Succeeded: true, id);
        }
        catch (OperationCanceledException)
        {
            return PathLookup.Failed;
        }
        catch (Exception exception) when (DataLoadFailure.IsExpected(exception))
        {
            LogPathLookupFailed(_logger, absolutePath, exception);
            Clear();
            return PathLookup.Failed;
        }
    }

    /// <summary>
    /// Liest eine Datei, die der Indexer noch nicht kennt, direkt von der Platte.
    /// </summary>
    /// <remarks>
    /// Wie beim indizierten Weg gilt: alles oder nichts. Gelingt das Lesen oder Übersetzen nicht,
    /// wird der Bereich geleert, statt still das vorige Dokument weiterzuzeigen. Die Bezüge gehören
    /// immer dem vorigen Dokument; eine nicht indizierte Datei hat keine.
    /// </remarks>
    private async Task LoadDirectFromFileAsync(string absolutePath, LatestOperation.Ticket load)
    {
        UnindexedFile? file = await _unindexedFiles.ReadAsync(absolutePath, load).ConfigureAwait(true);
        if (!load.IsCurrent)
        {
            return;
        }
        if (file is null)
        {
            Clear();
            return;
        }

        Preview.ShowUnindexedFile(file.Parsed.RenderedHtmlGz, absolutePath);
        // Der Editor bekommt keine MarkdownFileId: Eine erfundene hätte im Index kein Gegenstück.
        // Bearbeiten und Speichern laufen über den Pfad; den Index zieht der Datei-Watcher nach.
        Editor.LoadDirect(absolutePath, file.Bytes);
        Relations.Clear();
        LogDirectLoadCompleted(_logger, absolutePath);
    }

    private void OnRelatedDocumentRequested(Guid markdownFileId) => RelatedDocumentRequested?.Invoke(markdownFileId);

    private void OnFolderRequested(string folderPath) => FolderRequested?.Invoke(folderPath);

    private void OnTagRequested(string tagSlug) => TagRequested?.Invoke(tagSlug);

    private void OnDocumentChanged(string? newAbsolutePath) => DocumentChanged?.Invoke(newAbsolutePath);

    private void OnEditorSaved(object? sender, EditorSavedEventArgs args)
    {
        try
        {
            ParseResult result = _markdownParser.Parse(args.SavedText);
            Preview.ShowRendered(result.RenderedHtmlGz);
        }
        catch (Exception exception) when (BackgroundServiceWatchdog.IsRecoverable(exception, CancellationToken.None))
        {
            // Die Datei liegt zu diesem Zeitpunkt schon gespeichert auf der Platte. Weist der
            // Parser den Text ab, bleibt nur die Vorschau auf dem alten Stand; das Speichern
            // selbst darf daran nicht nachträglich scheitern.
            LogPreviewRefreshFailed(_logger, exception);
        }
    }

    /// <summary>Was das Nachschlagen im Index ergeben hat.</summary>
    /// <param name="Succeeded">Ob die Antwort gilt; sonst ist der Vorgang abgebrochen oder gescheitert.</param>
    /// <param name="IndexedId">Die Kennung, wenn die Datei im Index steht.</param>
    private sealed record PathLookup(bool Succeeded, Guid? IndexedId)
    {
        public static PathLookup Failed { get; } = new(Succeeded: false, IndexedId: null);
    }
}
