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
/// Umweg über eine Suche. Deshalb ist hier jeder Eintrag ein Weg und keine Angabe — wer eine
/// Zeile liest und danach doch suchen muss, hat nichts gewonnen.
/// </remarks>
internal sealed partial class DocumentRelationsViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDocumentFileService _fileService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<DocumentRelationsViewModel> _logger;

    /// <summary>
    /// Der Merker, der beim Beenden der Anwendung anschlägt.
    /// </summary>
    /// <remarks>
    /// Er kommt aus dem Wirt (<c>IHostApplicationLifetime.ApplicationStopping</c>). Ohne ihn
    /// stand hier <c>CancellationToken.None</c> — dann wartet das Schließen auf Arbeit, die
    /// niemand mehr braucht. Ohne Wirt, also im Test, bleibt er der leere Merker.
    /// </remarks>
    private readonly CancellationToken _shutdownToken;

    private Guid _openDocumentId;
    private string _openAbsolutePath = string.Empty;

    [ObservableProperty]
    private DocumentRelationsState _state = DocumentRelationsState.NoDocument;

    [ObservableProperty]
    private string _folderPath = string.Empty;

    /// <summary>Der neue Name, solange er noch getippt wird.</summary>
    [ObservableProperty]
    private string _newName = string.Empty;

    /// <summary>Was der letzte Vorgang bewirkt hat — in einem Satz.</summary>
    [ObservableProperty]
    private string _operationMessage = string.Empty;

    /// <summary>Erzeugt das ViewModel.</summary>
    public DocumentRelationsViewModel(
        IServiceScopeFactory scopeFactory,
        IDocumentFileService fileService,
        IDialogService dialogService,
        ILogger<DocumentRelationsViewModel> logger,
        CancellationToken shutdownToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(fileService);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _fileService = fileService;
        _dialogService = dialogService;
        _logger = logger;
        _shutdownToken = shutdownToken;
        Outgoing = [];
        Incoming = [];
        Tags = [];
        OpenRelatedCommand = new RelayCommand<RelatedDocumentViewModel>(RaiseOpenRequested);
        ShowFolderCommand = new RelayCommand(RaiseFolderRequested, () => FolderPath.Length > 0);
        ShowTagCommand = new RelayCommand<string>(RaiseTagRequested);
        RenameCommand = new AsyncRelayCommand(RenameAsync, () => HasDocument && NewName.Trim().Length > 0);
        MoveCommand = new AsyncRelayCommand(MoveAsync, () => HasDocument);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => HasDocument);
    }

    /// <summary>Wird ausgelöst, wenn ein verwandtes Dokument geöffnet werden soll.</summary>
    public event Action<Guid>? OpenRequested;

    /// <summary>Wird ausgelöst, wenn der Ordner des Dokuments gezeigt werden soll.</summary>
    public event Action<string>? FolderRequested;

    /// <summary>Wird ausgelöst, wenn eine Kennzeichnung gezeigt werden soll.</summary>
    public event Action<string>? TagRequested;

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

    /// <summary>Benennt die Datei um — aus dem Zusammenhang heraus, ohne Verwaltungsseite.</summary>
    public AsyncRelayCommand RenameCommand { get; }

    /// <summary>Verschiebt die Datei in ein anderes Verzeichnis.</summary>
    public AsyncRelayCommand MoveCommand { get; }

    /// <summary>Löscht die Datei, nachdem die Folgen benannt wurden.</summary>
    public AsyncRelayCommand DeleteCommand { get; }

    /// <summary>Wird ausgelöst, wenn ein Vorgang die Datei verändert hat.</summary>
    /// <remarks>
    /// Der Weg zurück in die Ansicht: Nach dem Umbenennen oder Verschieben zeigt derselbe
    /// Eintrag auf einen neuen Pfad, nach dem Löschen auf gar keinen mehr.
    /// </remarks>
    public event Action<string?>? DocumentChanged;

    /// <summary>Es ist kein Dokument geöffnet.</summary>
    public bool ShowsNoDocument => State == DocumentRelationsState.NoDocument;

    /// <summary>Es ist ein Dokument geöffnet — der Bereich zeigt sich überhaupt.</summary>
    public bool HasDocument => State != DocumentRelationsState.NoDocument;

    /// <summary>Das Dokument verweist auf etwas.</summary>
    public bool HasOutgoing => Outgoing.Count > 0;

    /// <summary>Auf das Dokument wird verwiesen.</summary>
    public bool HasIncoming => Incoming.Count > 0;

    /// <summary>Die Verbindungen werden gerade ermittelt.</summary>
    public bool IsLoading => State == DocumentRelationsState.Loading;

    /// <summary>Das Dokument hat weder Verweise noch Kennzeichnungen.</summary>
    public bool ShowsNothingRelated => State == DocumentRelationsState.NothingRelated;

    /// <summary>Es gibt etwas zu zeigen.</summary>
    public bool ShowsRelations => State == DocumentRelationsState.Relations;

    /// <summary>Vergisst das zuletzt geladene Dokument.</summary>
    public void Clear()
    {
        Outgoing.Clear();
        Incoming.Clear();
        Tags.Clear();
        FolderPath = string.Empty;
        NewName = string.Empty;
        OperationMessage = string.Empty;
        _openDocumentId = Guid.Empty;
        _openAbsolutePath = string.Empty;
        ShowFolderCommand.NotifyCanExecuteChanged();
        State = DocumentRelationsState.NoDocument;
    }

    /// <summary>
    /// Lädt die Verbindungen eines Dokuments.
    /// </summary>
    /// <param name="markdownFileId">Das geöffnete Dokument.</param>
    /// <param name="tags">Seine Kennzeichnungen — sie stehen im Text und nicht im Graphen.</param>
    /// <param name="cancellationToken">Abbruchmerker.</param>
    public async Task LoadAsync(Guid markdownFileId, IReadOnlyList<string> tags, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tags);

        if (markdownFileId == Guid.Empty)
        {
            Clear();
            return;
        }

        State = DocumentRelationsState.Loading;
        try
        {
            AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(true))
            {
                IGraphService graph = scope.ServiceProvider.GetRequiredService<IGraphService>();
                IMarkdownFileRepository files = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();

                DocumentRelations relations = await graph.GetRelationsAsync(markdownFileId, cancellationToken).ConfigureAwait(true);
                MarkdownFile? file = await files.GetByIdAsync(markdownFileId, cancellationToken).ConfigureAwait(true);

                _openDocumentId = markdownFileId;
                _openAbsolutePath = file?.AbsolutePath ?? string.Empty;
                NewName = file?.FileNameWithoutExtension ?? string.Empty;
                OperationMessage = string.Empty;
                Apply(relations, tags, file?.RelativePath ?? string.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            // Der Nutzer hat weitergeklickt — die Antwort gilt einem Dokument, das nicht mehr offen ist.
        }
        catch (InvalidOperationException exception)
        {
            LogRelationsFailed(_logger, markdownFileId, exception);
            State = DocumentRelationsState.Failed;
        }
    }

    /// <summary>Trennt den Ordneranteil eines relativen Pfads ab.</summary>
    private static string FolderOf(string relativePath)
    {
        int cut = relativePath.LastIndexOfAny(['/', '\\']);

        return cut < 0 ? string.Empty : relativePath[..cut].Replace('\\', '/');
    }

    /// <summary>Formuliert die Rückfrage vor dem Umbenennen samt Folgen.</summary>
    private static string RenameQuestion(DocumentImpact impact, string newName)
    {
        string subject = impact.Title.Length == 0 ? "Diese Datei" : $"„{impact.Title}“";
        string target = newName.Trim();

        return impact.IncomingLinkCount == 1
            ? $"{subject} wird in „{target}“ umbenannt. Ein Dokument verweist auf den bisherigen Namen — sein Verweis zeigt danach ins Leere."
            : $"{subject} wird in „{target}“ umbenannt. {impact.IncomingLinkCount} Dokumente verweisen auf den bisherigen Namen — ihre Verweise zeigen danach ins Leere.";
    }

    /// <summary>Formuliert die Rückfrage vor dem Löschen samt Folgen.</summary>
    private static string DeletionQuestion(DocumentImpact impact)
    {
        string subject = impact.Title.Length == 0 ? "Diese Datei" : $"„{impact.Title}“";

        return impact.IncomingLinkCount switch
        {
            0 => $"{subject} wird gelöscht. Kein anderes Dokument verweist darauf.",
            1 => $"{subject} wird gelöscht. Ein Dokument verweist darauf — sein Verweis zeigt danach ins Leere.",
            _ => $"{subject} wird gelöscht. {impact.IncomingLinkCount} Dokumente verweisen darauf — ihre Verweise zeigen danach ins Leere.",
        };
    }

    private static string? FolderOfPath(string absolutePath)
    {
        int cut = absolutePath.LastIndexOfAny(['/', '\\']);

        return cut < 0 ? null : absolutePath[..cut];
    }

    [LoggerMessage(EventId = 1400, Level = LogLevel.Warning, Message = "Zusammenhänge für Datei {MarkdownFileId} konnten nicht ermittelt werden.")]
    private static partial void LogRelationsFailed(ILogger logger, Guid markdownFileId, Exception exception);

    /// <summary>
    /// Benennt die Datei um.
    /// </summary>
    /// <remarks>
    /// Nachgefragt wird nur, wenn etwas daran hängt: Ein WikiLink zeigt auf den Dateinamen,
    /// und der ändert sich hier — die Verweise anderer Dokumente brechen also. Hängt nichts
    /// daran, wäre eine Rückfrage bloß Gewöhnung daran, Rückfragen wegzuklicken.
    /// </remarks>
    private async Task RenameAsync()
    {
        DocumentImpact impact = await _fileService
            .GetImpactAsync(_openDocumentId, _shutdownToken)
            .ConfigureAwait(true);

        if (impact.IncomingLinkCount > 0
            && !_dialogService.Confirm("Datei umbenennen", RenameQuestion(impact, NewName)))
        {
            return;
        }

        DocumentFileResult result = await _fileService
            .RenameAsync(_openDocumentId, NewName, _shutdownToken)
            .ConfigureAwait(true);

        ReportAndFollow(result);
    }

    /// <summary>Verschiebt die Datei in ein anderes Verzeichnis.</summary>
    private async Task MoveAsync()
    {
        string? target = _dialogService.PickDirectory("Zielordner wählen", FolderOfPath(_openAbsolutePath));
        if (target is null)
        {
            return;
        }

        DocumentFileResult result = await _fileService
            .MoveAsync(_openDocumentId, target, _shutdownToken)
            .ConfigureAwait(true);

        ReportAndFollow(result);
    }

    /// <summary>
    /// Löscht die Datei, nachdem die Folgen benannt wurden.
    /// </summary>
    /// <remarks>
    /// Die Rückfrage nennt, wie viele Dokumente danach ins Leere zeigen. Wer das erst
    /// hinterher erfährt, kann es nicht mehr abwählen — und ein Verweis, der stillschweigend
    /// bricht, fällt erst Monate später auf.
    /// </remarks>
    private async Task DeleteAsync()
    {
        DocumentImpact impact = await _fileService
            .GetImpactAsync(_openDocumentId, _shutdownToken)
            .ConfigureAwait(true);

        if (!_dialogService.Confirm("Datei löschen", DeletionQuestion(impact)))
        {
            return;
        }

        DocumentFileResult result = await _fileService
            .DeleteAsync(_openDocumentId, _shutdownToken)
            .ConfigureAwait(true);

        ReportAndFollow(result);
    }

    private void Apply(DocumentRelations relations, IReadOnlyList<string> tags, string relativePath)
    {
        Outgoing.Clear();
        foreach (RelatedDocument document in relations.Outgoing)
        {
            Outgoing.Add(new RelatedDocumentViewModel(document));
        }

        Incoming.Clear();
        foreach (RelatedDocument document in relations.Incoming)
        {
            Incoming.Add(new RelatedDocumentViewModel(document));
        }

        Tags.Clear();
        foreach (string tag in tags.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            Tags.Add(tag);
        }

        FolderPath = FolderOf(relativePath);
        ShowFolderCommand.NotifyCanExecuteChanged();

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

    private void ReportAndFollow(DocumentFileResult result)
    {
        OperationMessage = result.Message;
        if (!result.Succeeded)
        {
            return;
        }

        DocumentChanged?.Invoke(result.NewAbsolutePath);
    }

    partial void OnNewNameChanged(string value) => RenameCommand.NotifyCanExecuteChanged();

    partial void OnStateChanged(DocumentRelationsState value)
    {
        RenameCommand.NotifyCanExecuteChanged();
        MoveCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowsNoDocument));
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(ShowsNothingRelated));
        OnPropertyChanged(nameof(ShowsRelations));
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

    /// <summary>Stabiler Schlüssel — Eingang für das Öffnen.</summary>
    public Guid MarkdownFileId { get; }

    /// <summary>Dateiname ohne Erweiterung.</summary>
    public string Title { get; }

    /// <summary>Pfad relativ zur Wurzel — unterscheidet gleichnamige Dateien.</summary>
    public string RelativePath { get; }
}

/// <summary>Was der Zusammenhangs-Bereich gerade zeigt.</summary>
internal enum DocumentRelationsState
{
    /// <summary>Es ist kein Dokument geöffnet.</summary>
    NoDocument = 0,

    /// <summary>Die Verbindungen werden ermittelt.</summary>
    Loading = 1,

    /// <summary>Das Dokument steht für sich — keine Verweise, keine Kennzeichnungen.</summary>
    NothingRelated = 2,

    /// <summary>Es gibt Verbindungen zu zeigen.</summary>
    Relations = 3,

    /// <summary>Die Verbindungen ließen sich nicht ermitteln.</summary>
    Failed = 4,
}
