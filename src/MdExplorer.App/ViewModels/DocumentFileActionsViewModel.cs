using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Localization;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Umbenennen, Verschieben und Löschen der geöffneten Datei, aus dem Zusammenhang heraus und
/// ohne Verwaltungsseite.
/// </summary>
/// <remarks>
/// Die Befehle wirken nur auf eine Datei, die ihnen ausdrücklich übergeben wurde
/// (<see cref="Target"/>). Solange die Bezüge laden oder gescheitert sind, gibt es keine; ein
/// Befehl träfe sonst die vorige Datei, die der Nutzer gar nicht mehr sieht.
/// </remarks>
internal sealed partial class DocumentFileActionsViewModel : ObservableObject
{
    private readonly IDocumentFileService _fileService;
    private readonly IDialogService _dialogService;
    private readonly IUnsavedChangesGuard _unsavedChanges;
    private readonly CancellationToken _shutdownToken;

    private Guid _documentId;
    private string _absolutePath = string.Empty;

    // Die Erfolgsmeldung eines Umbenennens oder Verschiebens, bis das Dokument unter seinem neuen
    // Pfad wieder geladen ist. Das Neuladen richtet die Aktionen neu aus und leerte sie sonst.
    private string? _carriedMessagePath;
    private string _carriedMessage = string.Empty;

    /// <summary>Der neue Name, solange er noch getippt wird.</summary>
    [ObservableProperty]
    private string _newName = string.Empty;

    /// <summary>Was der letzte Vorgang bewirkt hat, in einem Satz.</summary>
    [ObservableProperty]
    private string _operationMessage = string.Empty;

    /// <summary>Erzeugt die Befehle.</summary>
    /// <param name="fileService">Führt die Dateivorgänge aus und nennt ihre Folgen.</param>
    /// <param name="dialogService">Stellt die Rückfragen.</param>
    /// <param name="unsavedChanges">Fragt vor jedem Vorgang nach ungespeicherten Änderungen.</param>
    /// <param name="shutdownToken">Schlägt beim Beenden der Anwendung an.</param>
    public DocumentFileActionsViewModel(
        IDocumentFileService fileService,
        IDialogService dialogService,
        IUnsavedChangesGuard unsavedChanges,
        CancellationToken shutdownToken)
    {
        ArgumentNullException.ThrowIfNull(fileService);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(unsavedChanges);

        _fileService = fileService;
        _dialogService = dialogService;
        _unsavedChanges = unsavedChanges;
        _shutdownToken = shutdownToken;
        RenameCommand = new AsyncRelayCommand(RenameAsync, () => HasTarget && FileNameRule.IsPlainName(NewName));
        MoveCommand = new AsyncRelayCommand(MoveAsync, () => HasTarget);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => HasTarget);
    }

    /// <summary>Wird ausgelöst, wenn ein Vorgang die Datei verändert hat.</summary>
    /// <remarks>
    /// Der Weg zurück in die Ansicht: Nach dem Umbenennen oder Verschieben zeigt derselbe
    /// Eintrag auf einen neuen Pfad, nach dem Löschen auf gar keinen mehr.
    /// </remarks>
    public event Action<string?>? DocumentChanged;

    /// <summary>Benennt die Datei um.</summary>
    public AsyncRelayCommand RenameCommand { get; }

    /// <summary>Verschiebt die Datei in ein anderes Verzeichnis.</summary>
    public AsyncRelayCommand MoveCommand { get; }

    /// <summary>Löscht die Datei, nachdem die Folgen benannt wurden.</summary>
    public AsyncRelayCommand DeleteCommand { get; }

    /// <summary>Der getippte Name enthält einen Pfad oder ein Zeichen, das kein Dateiname tragen darf.</summary>
    public bool ShowsInvalidName => NewName.Trim().Length > 0 && !FileNameRule.IsPlainName(NewName);

    private bool HasTarget => _documentId != Guid.Empty;

    /// <summary>Richtet die Befehle auf eine geladene Datei.</summary>
    /// <param name="documentId">Die Kennung im Index.</param>
    /// <param name="absolutePath">Der Pfad; sein Ordner ist der Startpunkt der Ordnerwahl beim Verschieben.</param>
    /// <param name="currentName">Der Dateiname ohne Endung, als Vorschlag zum Umbenennen.</param>
    public void Target(Guid documentId, string absolutePath, string currentName)
    {
        ArgumentNullException.ThrowIfNull(absolutePath);
        ArgumentNullException.ThrowIfNull(currentName);

        SetTarget(documentId, absolutePath, currentName);
    }

    /// <summary>Löst die Befehle von jeder Datei; danach sind sie gesperrt.</summary>
    public void Release() => SetTarget(Guid.Empty, string.Empty, string.Empty);

    /// <summary>Formuliert die Rückfrage vor dem Umbenennen samt Folgen.</summary>
    private static string RenameQuestion(DocumentImpact impact, string newName)
    {
        string subject = QuestionSubject(impact);
        string target = newName.Trim();

        return impact.IncomingLinkCount == 1
            ? UiText.Format("Relations_RenameQuestion_One", subject, target)
            : UiText.Format("Relations_RenameQuestion_Many", subject, target, impact.IncomingLinkCount);
    }

    /// <summary>Formuliert die Rückfrage vor dem Löschen samt Folgen.</summary>
    private static string DeletionQuestion(DocumentImpact impact)
    {
        string subject = QuestionSubject(impact);

        return impact.IncomingLinkCount switch
        {
            0 => UiText.Format("Relations_DeleteQuestion_None", subject),
            1 => UiText.Format("Relations_DeleteQuestion_One", subject),
            _ => UiText.Format("Relations_DeleteQuestion_Many", subject, impact.IncomingLinkCount),
        };
    }

    /// <summary>Nennt die Datei in der Rückfrage beim Titel, ohne Titel allgemein.</summary>
    private static string QuestionSubject(DocumentImpact impact)
    {
        return impact.Title.Length == 0
            ? UiText.Get("Relations_Subject_ThisFile")
            : UiText.Format("Relations_Subject_Titled", impact.Title);
    }

    private static string? FolderOfPath(string absolutePath)
    {
        int cut = absolutePath.LastIndexOfAny(['/', '\\']);

        return cut < 0 ? null : absolutePath[..cut];
    }

    private void SetTarget(Guid documentId, string absolutePath, string currentName)
    {
        _documentId = documentId;
        _absolutePath = absolutePath;
        NewName = currentName;
        OperationMessage = TakeCarriedMessage(absolutePath);
        MoveCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Benennt die Datei um.
    /// </summary>
    /// <remarks>
    /// Nachgefragt wird nur, wenn etwas daran hängt: Ein WikiLink zeigt auf den Dateinamen,
    /// und der ändert sich hier; die Verweise anderer Dokumente brechen also. Hängt nichts
    /// daran, wäre eine Rückfrage bloß Gewöhnung daran, Rückfragen wegzuklicken.
    /// </remarks>
    private async Task RenameAsync()
    {
        if (!await _unsavedChanges.ConfirmLeaveAsync(_shutdownToken).ConfigureAwait(true))
        {
            return;
        }

        if (await ReadImpactAsync().ConfigureAwait(true) is not { } impact)
        {
            return;
        }
        if (impact.IncomingLinkCount > 0
            && !_dialogService.Confirm(UiText.Get("Relations_RenameDialog_Title"), RenameQuestion(impact, NewName)))
        {
            return;
        }

        ReportAndFollow(await _fileService.RenameAsync(_documentId, NewName, _shutdownToken).ConfigureAwait(true));
    }

    private async Task MoveAsync()
    {
        if (!await _unsavedChanges.ConfirmLeaveAsync(_shutdownToken).ConfigureAwait(true))
        {
            return;
        }

        string? target = _dialogService.PickDirectory(UiText.Get("Relations_MoveDialog_Title"), FolderOfPath(_absolutePath));
        if (target is null)
        {
            return;
        }

        ReportAndFollow(await _fileService.MoveAsync(_documentId, target, _shutdownToken).ConfigureAwait(true));
    }

    /// <summary>
    /// Löscht die Datei, nachdem die Folgen benannt wurden.
    /// </summary>
    /// <remarks>
    /// Die Rückfrage nennt, wie viele Dokumente danach ins Leere zeigen. Wer das erst
    /// hinterher erfährt, kann es nicht mehr abwählen, und ein Verweis, der stillschweigend
    /// bricht, fällt erst Monate später auf.
    /// </remarks>
    private async Task DeleteAsync()
    {
        // Auch vor dem Löschen: Wer gespeichert haben will, bevor die Datei geht, soll es können;
        // wer verwirft, bekommt danach die gewohnte Rückfrage mit den Folgen.
        if (!await _unsavedChanges.ConfirmLeaveAsync(_shutdownToken).ConfigureAwait(true))
        {
            return;
        }

        if (await ReadImpactAsync().ConfigureAwait(true) is not { } impact)
        {
            return;
        }
        if (!_dialogService.Confirm(UiText.Get("Relations_DeleteDialog_Title"), DeletionQuestion(impact)))
        {
            return;
        }

        ReportAndFollow(await _fileService.DeleteAsync(_documentId, _shutdownToken).ConfigureAwait(true));
    }

    /// <summary>Liest die Folgen; ist der Index nicht lesbar, steht das als Meldung da.</summary>
    private async Task<DocumentImpact?> ReadImpactAsync()
    {
        DocumentImpact? impact = await _fileService.GetImpactAsync(_documentId, _shutdownToken).ConfigureAwait(true);
        if (impact is null)
        {
            OperationMessage = UiText.Get("DocumentFile_IndexUnavailable");
        }
        return impact;
    }

    /// <remarks>
    /// Nach dem Löschen gibt es kein Dokument mehr und damit keinen Bereich, in dem die Meldung
    /// stünde; dass die Datei aus Vorschau und Liste verschwindet, ist dort die Rückmeldung.
    /// Eine Fehlermeldung bleibt stehen, weil auf einen gescheiterten Vorgang nichts neu lädt.
    /// </remarks>
    private void ReportAndFollow(DocumentFileResult result)
    {
        OperationMessage = result.Message;
        if (!result.Succeeded)
        {
            return;
        }

        _carriedMessagePath = result.NewAbsolutePath;
        _carriedMessage = result.Message;
        DocumentChanged?.Invoke(result.NewAbsolutePath);
    }

    /// <summary>
    /// Liefert die mitgetragene Meldung, wenn das neue Ziel das Dokument unter seinem neuen Pfad
    /// ist. Ein anderes Dokument verwirft sie; das bloße Freigeben dazwischen behält sie.
    /// </summary>
    private string TakeCarriedMessage(string absolutePath)
    {
        if (absolutePath.Length == 0)
        {
            return string.Empty;
        }

        string message = string.Equals(absolutePath, _carriedMessagePath, StringComparison.OrdinalIgnoreCase)
            ? _carriedMessage
            : string.Empty;
        _carriedMessagePath = null;
        _carriedMessage = string.Empty;
        return message;
    }

    partial void OnNewNameChanged(string value)
    {
        RenameCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowsInvalidName));
    }
}
