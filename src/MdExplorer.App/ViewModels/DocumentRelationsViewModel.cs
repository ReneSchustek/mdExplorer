using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Graph.Abstractions;
using MdExplorer.Graph.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Zeigt, was am geöffneten Dokument hängt: Verweise in beide Richtungen, sein Ordner und
/// seine Kennzeichnungen.
/// </summary>
/// <remarks>
/// Der Prüfsatz der Gestaltungslinie lautet: von jedem Ding zu jedem verwandten Ding, ohne
/// Umweg über eine Suche. Deshalb ist hier jeder Eintrag ein Weg und keine Angabe. Wer eine
/// Zeile liest und danach doch suchen muss, hat nichts gewonnen. Umbenennen, Verschieben und
/// Löschen liegen in <see cref="FileActions"/>.
/// </remarks>
internal sealed partial class DocumentRelationsViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentRelationsViewModel> _logger;

    // Zählt die Ladevorgänge. Ein abgebrochener Vorgang räumt nur auf, solange kein neuerer
    // begonnen hat; sonst löschte er dessen Stand.
    private long _loadNumber;

    [ObservableProperty]
    private DocumentRelationsState _state = DocumentRelationsState.NoDocument;

    [ObservableProperty]
    private string _folderPath = string.Empty;

    /// <summary>Erzeugt das ViewModel.</summary>
    /// <param name="scopeFactory">Liefert je Ladevorgang Graph und Dateiverzeichnis.</param>
    /// <param name="fileService">Führt die Dateivorgänge aus.</param>
    /// <param name="dialogService">Stellt die Rückfragen der Dateivorgänge.</param>
    /// <param name="unsavedChanges">Fragt vor jedem Dateivorgang nach ungespeicherten Änderungen.</param>
    /// <param name="logger">Protokoll.</param>
    /// <param name="shutdownToken">
    /// Schlägt beim Beenden der Anwendung an (<c>IHostApplicationLifetime.ApplicationStopping</c>);
    /// ohne Wirt, also im Test, der leere Merker.
    /// </param>
    public DocumentRelationsViewModel(
        IServiceScopeFactory scopeFactory,
        IDocumentFileService fileService,
        IDialogService dialogService,
        IUnsavedChangesGuard unsavedChanges,
        ILogger<DocumentRelationsViewModel> logger,
        CancellationToken shutdownToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _logger = logger;
        FileActions = new DocumentFileActionsViewModel(fileService, dialogService, unsavedChanges, shutdownToken);
        FileActions.DocumentChanged += path => DocumentChanged?.Invoke(path);
        Outgoing = [];
        Incoming = [];
        Tags = [];
        OpenRelatedCommand = new RelayCommand<RelatedDocumentViewModel>(RaiseOpenRequested);
        ShowFolderCommand = new RelayCommand(RaiseFolderRequested, () => FolderPath.Length > 0);
        ShowTagCommand = new RelayCommand<string>(RaiseTagRequested);
    }

    /// <summary>Wird ausgelöst, wenn ein verwandtes Dokument geöffnet werden soll.</summary>
    public event Action<Guid>? OpenRequested;

    /// <summary>Wird ausgelöst, wenn der Ordner des Dokuments gezeigt werden soll.</summary>
    public event Action<string>? FolderRequested;

    /// <summary>Wird ausgelöst, wenn eine Kennzeichnung gezeigt werden soll.</summary>
    public event Action<string>? TagRequested;

    /// <summary>Wird ausgelöst, wenn ein Dateivorgang die Datei verändert hat, siehe <see cref="DocumentFileActionsViewModel.DocumentChanged"/>.</summary>
    public event Action<string?>? DocumentChanged;

    /// <summary>Umbenennen, Verschieben und Löschen der geöffneten Datei.</summary>
    public DocumentFileActionsViewModel FileActions { get; }

    /// <summary>Dokumente, auf die dieses verweist.</summary>
    public ObservableCollection<RelatedDocumentViewModel> Outgoing { get; }

    /// <summary>Dokumente, die auf dieses verweisen.</summary>
    public ObservableCollection<RelatedDocumentViewModel> Incoming { get; }

    /// <summary>Kennzeichnungen des Dokuments.</summary>
    public ObservableCollection<string> Tags { get; }

    /// <summary>Öffnet ein verwandtes Dokument.</summary>
    public RelayCommand<RelatedDocumentViewModel> OpenRelatedCommand { get; }

    /// <summary>Zeigt, was sonst noch im selben Ordner liegt.</summary>
    public RelayCommand ShowFolderCommand { get; }

    /// <summary>Zeigt, was dieselbe Kennzeichnung trägt.</summary>
    public RelayCommand<string> ShowTagCommand { get; }

    /// <summary>Es ist kein Dokument geöffnet.</summary>
    public bool ShowsNoDocument => State == DocumentRelationsState.NoDocument;

    /// <summary>Es ist ein Dokument geöffnet; der Bereich zeigt sich überhaupt.</summary>
    public bool HasDocument => State != DocumentRelationsState.NoDocument;

    /// <summary>Das Dokument verweist auf etwas.</summary>
    public bool HasOutgoing => Outgoing.Count > 0;

    /// <summary>Auf das Dokument wird verwiesen.</summary>
    public bool HasIncoming => Incoming.Count > 0;

    /// <summary>Die Verbindungen werden gerade ermittelt.</summary>
    public bool IsLoading => State == DocumentRelationsState.Loading;

    /// <summary>Das Dokument hat weder Verweise noch Kennzeichnungen und liegt direkt in der Wurzel.</summary>
    public bool ShowsNothingRelated => State == DocumentRelationsState.NothingRelated;

    /// <summary>Es gibt etwas zu zeigen.</summary>
    public bool ShowsRelations => State == DocumentRelationsState.Relations;

    /// <summary>Die Verbindungen ließen sich nicht ermitteln.</summary>
    /// <remarks>
    /// Ohne eigene Anzeige stünde nur die Überschrift da, und der Bereich sähe aus wie ein
    /// Dokument, an dem nichts hängt.
    /// </remarks>
    public bool ShowsFailure => State == DocumentRelationsState.Failed;

    /// <summary>Vergisst das zuletzt geladene Dokument.</summary>
    public void Clear()
    {
        Outgoing.Clear();
        Incoming.Clear();
        Tags.Clear();
        FolderPath = string.Empty;
        FileActions.Release();
        ShowFolderCommand.NotifyCanExecuteChanged();
        State = DocumentRelationsState.NoDocument;
    }

    /// <summary>
    /// Lädt die Verbindungen eines Dokuments.
    /// </summary>
    /// <param name="markdownFileId">Das geöffnete Dokument.</param>
    /// <param name="tags">Seine Kennzeichnungen. Sie stehen im Text und nicht im Graphen.</param>
    /// <param name="cancellationToken">Abbruchmerker.</param>
    public async Task LoadAsync(Guid markdownFileId, IReadOnlyList<string> tags, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tags);
        if (markdownFileId == Guid.Empty)
        {
            Clear();
            return;
        }
        long loadNumber = BeginLoad();
        try
        {
            await QueryAndShowAsync(markdownFileId, tags, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Hat ein neuerer Klick übernommen, zeigt der seinen eigenen Stand. Ohne ihn, etwa beim
            // Beenden, stünde sonst dauerhaft „lädt“ da.
            if (loadNumber == _loadNumber)
            {
                Clear();
            }
        }
        catch (Exception exception) when (DataLoadFailure.IsExpected(exception))
        {
            LogRelationsFailed(_logger, markdownFileId, exception);
            State = DocumentRelationsState.Failed;
        }
    }

    [LoggerMessage(EventId = 1400, Level = LogLevel.Warning, Message = "Zusammenhänge für Datei {MarkdownFileId} konnten nicht ermittelt werden.")]
    private static partial void LogRelationsFailed(ILogger logger, Guid markdownFileId, Exception exception);

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (T item in items)
        {
            target.Add(item);
        }
    }

    private async Task QueryAndShowAsync(Guid markdownFileId, IReadOnlyList<string> tags, CancellationToken cancellationToken)
    {
        (DocumentRelations relations, MarkdownFile? file) = await QueryAsync(markdownFileId, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        FileActions.Target(markdownFileId, file?.AbsolutePath ?? string.Empty, file?.FileNameWithoutExtension ?? string.Empty);
        Apply(relations, tags, file?.RelativePath ?? string.Empty);
    }

    /// <summary>Beginnt einen Ladevorgang und gibt seine Nummer zurück.</summary>
    /// <remarks>Die Dateibefehle gelten ab hier nicht mehr dem vorigen Dokument, auch wenn dieser Vorgang scheitert.</remarks>
    private long BeginLoad()
    {
        FileActions.Release();
        State = DocumentRelationsState.Loading;
        return ++_loadNumber;
    }

    private async Task<(DocumentRelations Relations, MarkdownFile? File)> QueryAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(true))
        {
            IGraphService graph = scope.ServiceProvider.GetRequiredService<IGraphService>();
            IMarkdownFileRepository files = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();

            DocumentRelations relations = await graph.GetRelationsAsync(markdownFileId, cancellationToken).ConfigureAwait(true);
            MarkdownFile? file = await files.GetByIdAsync(markdownFileId, cancellationToken).ConfigureAwait(true);
            return (relations, file);
        }
    }

    private void Apply(DocumentRelations relations, IReadOnlyList<string> tags, string relativePath)
    {
        Replace(Outgoing, relations.Outgoing.Select(document => new RelatedDocumentViewModel(document)));
        Replace(Incoming, relations.Incoming.Select(document => new RelatedDocumentViewModel(document)));
        Replace(Tags, tags.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));

        FolderPath = RelativeFolder.Of(relativePath);
        ShowFolderCommand.NotifyCanExecuteChanged();

        // Auch ein Ordner ist ein Weg zu Verwandtem; leer ist der Bereich erst ohne ihn.
        State = Outgoing.Count + Incoming.Count + Tags.Count == 0 && FolderPath.Length == 0
            ? DocumentRelationsState.NothingRelated
            : DocumentRelationsState.Relations;
    }

    private void RaiseOpenRequested(RelatedDocumentViewModel? document)
    {
        if (document is null)
        {
            return;
        }

        OpenRequested?.Invoke(document.MarkdownFileId);
    }

    private void RaiseFolderRequested()
    {
        if (FolderPath.Length == 0)
        {
            return;
        }

        FolderRequested?.Invoke(FolderPath);
    }

    private void RaiseTagRequested(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        TagRequested?.Invoke(tag);
    }

    partial void OnStateChanged(DocumentRelationsState value)
    {
        OnPropertyChanged(nameof(ShowsNoDocument));
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(ShowsNothingRelated));
        OnPropertyChanged(nameof(ShowsRelations));
        OnPropertyChanged(nameof(ShowsFailure));
        OnPropertyChanged(nameof(HasOutgoing));
        OnPropertyChanged(nameof(HasIncoming));
    }
}

/// <summary>Ein Dokument am anderen Ende einer Verbindung, wie es die Ansicht zeigt.</summary>
internal sealed class RelatedDocumentViewModel
{
    /// <summary>Erzeugt den Eintrag aus einem Ergebnis des Graphen.</summary>
    public RelatedDocumentViewModel(RelatedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        MarkdownFileId = document.MarkdownFileId;
        Title = document.Title;
        RelativePath = document.RelativePath;
    }

    /// <summary>Stabiler Schlüssel, Eingang für das Öffnen.</summary>
    public Guid MarkdownFileId { get; }

    /// <summary>Dateiname ohne Erweiterung.</summary>
    public string Title { get; }

    /// <summary>Pfad relativ zur Wurzel; unterscheidet gleichnamige Dateien.</summary>
    public string RelativePath { get; }
}

/// <summary>Was der Zusammenhangs-Bereich gerade zeigt.</summary>
internal enum DocumentRelationsState
{
    /// <summary>Es ist kein Dokument geöffnet.</summary>
    NoDocument = 0,

    /// <summary>Die Verbindungen werden ermittelt.</summary>
    Loading = 1,

    /// <summary>Das Dokument steht für sich: keine Verweise, keine Kennzeichnungen, kein Unterordner.</summary>
    NothingRelated = 2,

    /// <summary>Es gibt Verbindungen zu zeigen.</summary>
    Relations = 3,

    /// <summary>Die Verbindungen ließen sich nicht ermitteln.</summary>
    Failed = 4,
}
