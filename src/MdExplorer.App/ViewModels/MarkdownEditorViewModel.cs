using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Localization;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Text;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Services;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// ViewModel des Markdown-Editors. Verwaltet den Rohtext einer Markdown-Datei, trägt den
/// Dirty-Status und speichert atomar. Die Kennzeichnungen liest <see cref="DebouncedTagReader"/>,
/// Zeilenende, Byte-Order-Mark und Prüfsumme der Datei hält <see cref="EditorFileBaseline"/>.
/// Tag-CRUD (Hinzufügen, Umbenennen, Entfernen) läuft über <see cref="TagDocumentEditor"/> auf dem
/// Roh-Text, keine Geschäftslogik im Code-Behind.
/// </summary>
internal sealed partial class MarkdownEditorViewModel : ObservableObject, IDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MarkdownEditorViewModel> _logger;
    private readonly IEditorConfirmationDialogService _confirmationDialog;
    private readonly DebouncedTagReader _tagReader;

    private EditorFileBaseline _baseline = EditorFileBaseline.Empty;
    private bool _disposed;
    private bool _suppressDirtyTracking;

    // Nur eine Datei in gültigem UTF-8 lässt sich bearbeiten. Gespeichert wird immer UTF-8; aus
    // einer Datei in Windows-1252 machte das erste Speichern jedes Umlaut-Byte unwiderruflich zu
    // einem Ersatzzeichen.
    private bool _isWritableEncoding = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _text = string.Empty;

    // CanUnlock und CanSave hängen an der Datei. Ohne die Meldung blieben die Schaltflächen nach
    // dem Laden ausgegraut, weil sich das Gesperrt-Kennzeichen dabei nicht ändert.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnterEditModeCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _filePath;

    [ObservableProperty]
    private Guid _markdownFileId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTagCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnterEditModeCommand))]
    private bool _isLocked = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTagCommand))]
    private string _tagInput = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Erzeugt den Editor.</summary>
    public MarkdownEditorViewModel(
        IFileSystem fileSystem,
        ITagExtractor tagExtractor,
        TimeProvider timeProvider,
        IEditorConfirmationDialogService confirmationDialog,
        ILogger<MarkdownEditorViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(confirmationDialog);
        ArgumentNullException.ThrowIfNull(logger);

        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
        _logger = logger;
        _confirmationDialog = confirmationDialog;
        _tagReader = new DebouncedTagReader(tagExtractor, timeProvider);
    }

    /// <summary>Wird gefeuert, nachdem die Datei erfolgreich gespeichert wurde.</summary>
    public event EventHandler<EditorSavedEventArgs>? Saved;

    /// <summary>Aktuell im Dokument erkannte Tag-Namen (Original-Schreibweise).</summary>
    public ObservableCollection<string> Tags => _tagReader.Tags;

    /// <summary>
    /// Lädt die Datei in den Editor, erkennt EOL und initialisiert Dirty-Tracking.
    /// </summary>
    /// <remarks>
    /// Nach dem Lesen wird der Merker geprüft: Hat inzwischen ein neuerer Klick übernommen, darf
    /// diese Datei den Editor nicht mehr überschreiben.
    /// </remarks>
    public async Task LoadAsync(Guid markdownFileId, string absolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        byte[] bytes = await _fileSystem.ReadAllBytesAsync(absolutePath, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();

        ApplyLoadedContent(markdownFileId, absolutePath, bytes);
    }

    /// <summary>
    /// Übernimmt eine bereits gelesene Datei, die der Indexer noch nicht kennt.
    /// </summary>
    /// <remarks>
    /// Übergeben werden die gelesenen Bytes und nicht nur der Text: Nur über sie stimmen
    /// Prüfsumme und Byte-Order-Mark mit der Datei überein. Aus dem Text neu kodiert fehlte die
    /// Markierung, und das Speichern hielte die unveränderte Datei für fremd geändert.
    /// </remarks>
    /// <param name="absolutePath">Pfad der Datei.</param>
    /// <param name="bytes">Der gelesene Inhalt.</param>
    public void LoadDirect(string absolutePath, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentNullException.ThrowIfNull(bytes);

        ApplyLoadedContent(Guid.Empty, absolutePath, bytes);
    }

    /// <summary>Vergisst die offene Datei; der Editor ist danach leer und gesperrt.</summary>
    public void Clear()
    {
        _baseline = EditorFileBaseline.Empty;
        SetTextQuietly(Guid.Empty, null, string.Empty);
        IsDirty = false;
        IsLocked = true;
        StatusMessage = null;
        _tagReader.Reset(string.Empty);
    }

    /// <summary>Aktiviert den Bearbeiten-Modus (User-Klick auf "Bearbeiten").</summary>
    [RelayCommand(CanExecute = nameof(CanUnlock))]
    public void EnterEditMode()
    {
        // Dieselbe Bedingung wie die Freigabe des Befehls: Ein Tastenkürzel oder ein Aufruf aus
        // dem Panel geht an der Schaltfläche vorbei.
        if (!CanUnlock())
        {
            return;
        }
        IsLocked = false;
        StatusMessage = UiText.Get("Editor_Status_Editing");
    }

    /// <summary>Verlässt den Bearbeiten-Modus (zurück zur Anzeige).</summary>
    [RelayCommand]
    public void ExitEditMode()
    {
        IsLocked = true;
        StatusMessage = UiText.Get("Editor_Status_Viewing");
    }

    /// <summary>Persistiert den aktuellen Text atomar und reindiziert über den Datei-Watcher.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (FilePath is null)
        {
            return;
        }
        if (IsLocked)
        {
            StatusMessage = UiText.Get("Editor_Status_Locked");
            return;
        }

        if (!_confirmationDialog.ConfirmSave())
        {
            StatusMessage = UiText.Get("Editor_Status_SaveCancelled");
            return;
        }

        await PersistAsync(FilePath, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Speichert ohne die Rückfrage aus <see cref="SaveAsync"/> und auch im Anzeigen-Modus.
    /// </summary>
    /// <remarks>
    /// Für die Frage nach ungespeicherten Änderungen: Der Nutzer hat dort schon „Speichern“
    /// gewählt, eine zweite Rückfrage wäre dieselbe Frage noch einmal.
    /// </remarks>
    /// <returns><see langword="true"/>, wenn danach nichts mehr ungespeichert ist.</returns>
    public async Task<bool> SaveWithoutConfirmationAsync(CancellationToken cancellationToken)
    {
        if (FilePath is not null && IsDirty)
        {
            await PersistAsync(FilePath, cancellationToken).ConfigureAwait(true);
        }
        return !IsDirty;
    }

    /// <summary>Setzt den Text auf den zuletzt geladenen oder gespeicherten Stand zurück.</summary>
    public void DiscardChanges()
    {
        SetTextQuietly(MarkdownFileId, FilePath, _baseline.Text);
        IsDirty = false;
        // Auch eine noch laufende Wartezeit der Tag-Erkennung trüge sonst den verworfenen Text
        // nachträglich in die Kennzeichnungen.
        _tagReader.Reset(_baseline.Text);
    }

    /// <summary>Fügt einen Tag im Dokument-Kontext hinzu (siehe <see cref="TagDocumentEditor.Add"/>).</summary>
    [RelayCommand(CanExecute = nameof(CanAddTag))]
    public void AddTag()
    {
        string name = TagInput.Trim().TrimStart('#');
        if (IsLocked || !HashtagPattern.IsValidName(name))
        {
            return;
        }
        Text = TagDocumentEditor.Add(Text, name, [.. Tags]);
        TagInput = string.Empty;
    }

    /// <summary>Entfernt alle Vorkommen des Tags aus dem Dokument.</summary>
    [RelayCommand]
    public void RemoveTag(string? tagName)
    {
        if (IsLocked || string.IsNullOrWhiteSpace(tagName))
        {
            return;
        }
        Text = TagDocumentEditor.Remove(Text, tagName);
    }

    /// <summary>Benennt alle Vorkommen eines Tags lokal im Dokument um.</summary>
    public void RenameTag(string oldName, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        if (IsLocked)
        {
            return;
        }
        Text = TagDocumentEditor.Rename(Text, oldName, newName);
    }

    /// <summary>Hält den Zeitgeber der Tag-Erkennung an.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _tagReader.Dispose();
    }

    [LoggerMessage(EventId = 500, Level = LogLevel.Information, Message = "Datei {FilePath} gespeichert.")]
    private static partial void LogSaved(ILogger logger, string filePath);

    [LoggerMessage(EventId = 501, Level = LogLevel.Warning, Message = "Speichern abgebrochen: externe Änderung an {FilePath}.")]
    private static partial void LogConflict(ILogger logger, string filePath);

    [LoggerMessage(EventId = 502, Level = LogLevel.Error, Message = "Speichern von {FilePath} fehlgeschlagen.")]
    private static partial void LogSaveFailure(ILogger logger, Exception exception, string filePath);

    private async Task PersistAsync(string filePath, CancellationToken cancellationToken)
    {
        IsSaving = true;
        try
        {
            if (await _baseline.IsChangedOnDiskAsync(_fileSystem, filePath, cancellationToken).ConfigureAwait(true))
            {
                StatusMessage = UiText.Get("Editor_Status_ExternalConflict");
                LogConflict(_logger, filePath);
                return;
            }

            (string normalized, byte[] payload) = _baseline.Encode(Text);
            await _fileSystem.WriteAllBytesAtomicAsync(filePath, payload, cancellationToken).ConfigureAwait(true);
            MarkSaved(filePath, normalized, payload);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = UiText.Format("Editor_Status_SaveFailed", exception.Message);
            LogSaveFailure(_logger, exception, filePath);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void MarkSaved(string filePath, string normalized, byte[] payload)
    {
        _baseline = _baseline.AfterSave(Text, payload);
        IsDirty = false;
        StatusMessage = UiText.Format("Editor_Status_Saved", _timeProvider.GetLocalNow());
        LogSaved(_logger, filePath);
        Saved?.Invoke(this, new EditorSavedEventArgs(normalized));
    }

    private void ApplyLoadedContent(Guid markdownFileId, string absolutePath, byte[] bytes)
    {
        _isWritableEncoding = Utf8Decoder.TryDecodeStrict(bytes, out string text);
        _baseline = EditorFileBaseline.FromFile(bytes, text);
        SetTextQuietly(markdownFileId, absolutePath, text);

        IsDirty = false;
        IsLocked = true;
        StatusMessage = _isWritableEncoding ? null : UiText.Get("Editor_Status_NotUtf8");
        EnterEditModeCommand.NotifyCanExecuteChanged();
        _tagReader.Reset(text);
    }

    /// <summary>Setzt Datei und Text, ohne dass das als Änderung durch den Nutzer zählt.</summary>
    private void SetTextQuietly(Guid markdownFileId, string? filePath, string text)
    {
        _suppressDirtyTracking = true;
        try
        {
            MarkdownFileId = markdownFileId;
            FilePath = filePath;
            Text = text;
        }
        finally
        {
            _suppressDirtyTracking = false;
        }
    }

    private bool CanSave() => IsDirty && !IsSaving && !IsLocked && FilePath is not null;

    // Nur ein Name, der als Hashtag im Text bestehen kann; die Schaltfläche bleibt sonst aus.
    private bool CanAddTag() => !IsLocked && HashtagPattern.IsValidName(TagInput.Trim());

    private bool CanUnlock() => IsLocked && FilePath is not null && _isWritableEncoding;

    partial void OnTextChanged(string value)
    {
        if (_suppressDirtyTracking)
        {
            return;
        }
        IsDirty = !string.Equals(value, _baseline.Text, StringComparison.Ordinal);
        _tagReader.Schedule(value);
    }
}
