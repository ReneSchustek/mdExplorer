using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Text;
using MdExplorer.Parser.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// ViewModel des Markdown-Editors. Verwaltet den Rohtext einer Markdown-Datei,
/// erkennt Live-Tags mit Debounce, traegt Dirty-Status, persistiert atomar und bewahrt
/// das ursprueengliche Zeilenende. Tag-CRUD (Hinzufuegen, Umbenennen, Entfernen)
/// wird ueber <see cref="TagDocumentEditor"/> auf dem Roh-Text ausgefuehrt — keine
/// Geschaeftslogik im Code-Behind.
/// </summary>
internal sealed partial class MarkdownEditorViewModel : ObservableObject, IDisposable
{
    /// <summary>Standard-Debounce-Zeit fuer die Live-Tag-Extraktion.</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(300);

    private readonly IFileSystem _fileSystem;
    private readonly ITagExtractor _tagExtractor;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _debounce;
    private readonly ILogger<MarkdownEditorViewModel> _logger;
    private readonly IEditorConfirmationDialogService? _confirmationDialog;
    private readonly Lock _gate = new();
    private readonly object _tagsGate = new();

    private string _originalText = string.Empty;
    private string _originalContentHash = string.Empty;
    private LineEndingStyle _originalEol = LineEndingDetector.Default;
    private ITimer? _debounceTimer;
    private bool _disposed;
    private bool _suppressDirtyTracking;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _text = string.Empty;

    [ObservableProperty]
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

    /// <summary>Konstruktor mit Standard-Debounce.</summary>
    public MarkdownEditorViewModel(
        IFileSystem fileSystem,
        ITagExtractor tagExtractor,
        TimeProvider timeProvider,
        IEditorConfirmationDialogService confirmationDialog,
        ILogger<MarkdownEditorViewModel> logger)
        : this(fileSystem, tagExtractor, timeProvider, logger, DefaultDebounce, confirmationDialog)
    {
    }

    /// <summary>Konstruktor ohne Confirm-Dialog — Tests und Migrations-Pfade.</summary>
    public MarkdownEditorViewModel(
        IFileSystem fileSystem,
        ITagExtractor tagExtractor,
        TimeProvider timeProvider,
        ILogger<MarkdownEditorViewModel> logger)
        : this(fileSystem, tagExtractor, timeProvider, logger, DefaultDebounce, confirmationDialog: null)
    {
    }

    /// <summary>Konstruktor mit anpassbarem Debounce + optionalem Confirm-Dialog — fuer Tests.</summary>
    internal MarkdownEditorViewModel(
        IFileSystem fileSystem,
        ITagExtractor tagExtractor,
        TimeProvider timeProvider,
        ILogger<MarkdownEditorViewModel> logger,
        TimeSpan debounce,
        IEditorConfirmationDialogService? confirmationDialog = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(tagExtractor);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(debounce, TimeSpan.Zero);

        _fileSystem = fileSystem;
        _tagExtractor = tagExtractor;
        _timeProvider = timeProvider;
        _logger = logger;
        _debounce = debounce;
        _confirmationDialog = confirmationDialog;
        Tags = [];
        BindingOperations.EnableCollectionSynchronization(Tags, _tagsGate);
    }

    /// <summary>Aktuell im Dokument erkannte Tag-Namen (Original-Schreibweise).</summary>
    public ObservableCollection<string> Tags { get; }

    /// <summary>Wird gefeuert, nachdem die Datei erfolgreich gespeichert wurde.</summary>
    public event EventHandler<EditorSavedEventArgs>? Saved;

    /// <summary>Wird nach jedem Debounce-Lauf der Tag-Extraktion gefeuert — fuer Tests.</summary>
    public event EventHandler? TagsRefreshed;

    /// <summary>
    /// Laedt die Datei in den Editor, erkennt EOL und initialisiert Dirty-Tracking.
    /// </summary>
    public async Task LoadAsync(Guid markdownFileId, string absolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        byte[] bytes = await _fileSystem.ReadAllBytesAsync(absolutePath, cancellationToken).ConfigureAwait(true);
        string text = Utf8Decoder.DecodeNoBom(bytes);

        ApplyLoadedContent(markdownFileId, absolutePath, text, bytes);
    }

    /// <summary>
    /// Uebernimmt einen bereits geladenen Roh-Text (typischer Fall: Direct-Load durch
    /// <see cref="DocumentPanelViewModel"/>, weil der Indexer die Datei noch nicht kennt).
    /// Verhaelt sich danach wie ein normaler <see cref="LoadAsync"/>-Aufruf.
    /// </summary>
    public Task LoadDirectAsync(string absolutePath, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] payload = Encoding.UTF8.GetBytes(text);
        ApplyLoadedContent(Guid.Empty, absolutePath, text, payload);
        return Task.CompletedTask;
    }

    private void ApplyLoadedContent(Guid markdownFileId, string absolutePath, string text, byte[] payload)
    {
        _originalEol = LineEndingDetector.Detect(text);
        _originalContentHash = ComputeHash(payload);
        _originalText = text;

        _suppressDirtyTracking = true;
        try
        {
            MarkdownFileId = markdownFileId;
            FilePath = absolutePath;
            Text = text;
        }
        finally
        {
            _suppressDirtyTracking = false;
        }

        IsDirty = false;
        IsLocked = true;
        StatusMessage = null;
        UpdateTagsFromText(text);
    }

    /// <summary>Aktiviert den Bearbeiten-Modus (User-Klick auf "Bearbeiten").</summary>
    [RelayCommand(CanExecute = nameof(CanUnlock))]
    public void EnterEditMode()
    {
        if (FilePath is null)
        {
            return;
        }
        IsLocked = false;
        StatusMessage = "Bearbeitung aktiv — Strg+S oeffnet Speichern-Dialog.";
    }

    /// <summary>Verlaesst den Bearbeiten-Modus (zurueck zur Anzeige).</summary>
    [RelayCommand]
    public void ExitEditMode()
    {
        IsLocked = true;
        StatusMessage = "Anzeigen-Modus.";
    }

    /// <summary>Persistiert den aktuellen Text atomar und reindiziert ueber den Datei-Watcher.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (FilePath is null)
        {
            return;
        }
        if (IsLocked)
        {
            StatusMessage = "Datei ist im Anzeigen-Modus — bitte erst Bearbeiten.";
            return;
        }

        // Speichern mit Bestaetigungs-Dialog. Wenn kein Dialog-Service injiziert ist
        // (z. B. Tests), wird der Speichervorgang ohne Confirm fortgesetzt — die alte Semantik.
        if (_confirmationDialog is not null && !_confirmationDialog.ConfirmSave())
        {
            StatusMessage = "Speichern abgebrochen.";
            return;
        }

        IsSaving = true;
        try
        {
            await EnsureNoExternalChangeAsync(cancellationToken).ConfigureAwait(true);

            string normalized = LineEndingDetector.Normalize(Text, _originalEol);
            byte[] payload = Encoding.UTF8.GetBytes(normalized);
            await _fileSystem.WriteAllBytesAtomicAsync(FilePath, payload, cancellationToken).ConfigureAwait(true);

            _originalText = Text;
            _originalContentHash = ComputeHash(payload);
            IsDirty = false;
            StatusMessage = $"Gespeichert: {_timeProvider.GetLocalNow():HH:mm:ss}";
            LogSaved(_logger, FilePath);
            Saved?.Invoke(this, new EditorSavedEventArgs(Text));
        }
        catch (ExternalEditConflictException exception)
        {
            StatusMessage = exception.Message;
            LogConflict(_logger, FilePath);
        }
        catch (IOException exception)
        {
            StatusMessage = $"Speichern fehlgeschlagen: {exception.Message}";
            LogSaveFailure(_logger, exception, FilePath);
        }
        catch (UnauthorizedAccessException exception)
        {
            StatusMessage = $"Speichern fehlgeschlagen: {exception.Message}";
            LogSaveFailure(_logger, exception, FilePath);
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>Fuegt einen Tag im Dokument-Kontext hinzu (siehe <see cref="TagDocumentEditor.Add"/>).</summary>
    [RelayCommand(CanExecute = nameof(CanAddTag))]
    public void AddTag()
    {
        if (IsLocked)
        {
            return;
        }
        string name = TagInput.Trim().TrimStart('#');
        if (name.Length == 0)
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
        if (IsLocked)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(tagName))
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

    /// <summary>Loescht Debounce-Timer und meldet sich von Resources ab.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        DisposeTimer();
    }

    /// <summary>Liefert <see langword="true"/>, wenn ungespeicherte Aenderungen vorliegen — fuer Confirm-Dialog.</summary>
    public bool HasUnsavedChanges => IsDirty;

    private bool CanSave() => IsDirty && !IsSaving && !IsLocked && FilePath is not null;

    private bool CanAddTag() => !string.IsNullOrWhiteSpace(TagInput) && !IsLocked;

    private bool CanUnlock() => IsLocked && FilePath is not null;

    partial void OnTextChanged(string value)
    {
        if (_suppressDirtyTracking)
        {
            return;
        }
        IsDirty = !string.Equals(value, _originalText, StringComparison.Ordinal);
        ScheduleDebouncedTagExtraction(value);
    }

    private void ScheduleDebouncedTagExtraction(string text)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            DisposeTimer();
            if (_debounce == TimeSpan.Zero)
            {
                UpdateTagsFromText(text);
                TagsRefreshed?.Invoke(this, EventArgs.Empty);
                return;
            }
            _debounceTimer = _timeProvider.CreateTimer(OnDebounceElapsed, text, _debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        if (state is not string text || _disposed)
        {
            return;
        }
        UpdateTagsFromText(text);
        TagsRefreshed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateTagsFromText(string text)
    {
        IReadOnlyList<string> extracted = _tagExtractor.ExtractFromText(text);
        lock (_tagsGate)
        {
            Tags.Clear();
            foreach (string tag in extracted)
            {
                Tags.Add(tag);
            }
        }
    }

    private async Task EnsureNoExternalChangeAsync(CancellationToken cancellationToken)
    {
        if (FilePath is null || _originalContentHash.Length == 0)
        {
            return;
        }
        if (!_fileSystem.FileExists(FilePath))
        {
            return;
        }
        byte[] currentBytes = await _fileSystem.ReadAllBytesAsync(FilePath, cancellationToken).ConfigureAwait(true);
        string currentHash = ComputeHash(currentBytes);
        if (!string.Equals(currentHash, _originalContentHash, StringComparison.Ordinal))
        {
            throw new ExternalEditConflictException("Die Datei wurde extern geaendert. Aenderungen wurden NICHT gespeichert.");
        }
    }

    private static string ComputeHash(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private void DisposeTimer()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }

    [LoggerMessage(EventId = 500, Level = LogLevel.Information, Message = "Datei {FilePath} gespeichert.")]
    private static partial void LogSaved(ILogger logger, string filePath);

    [LoggerMessage(EventId = 501, Level = LogLevel.Warning, Message = "Speichern abgebrochen: externe Aenderung an {FilePath}.")]
    private static partial void LogConflict(ILogger logger, string filePath);

    [LoggerMessage(EventId = 502, Level = LogLevel.Error, Message = "Speichern von {FilePath} fehlgeschlagen.")]
    private static partial void LogSaveFailure(ILogger logger, Exception exception, string filePath);
}

/// <summary>Konflikt-Marker, wenn die Quelldatei zwischen Load und Save extern geaendert wurde.</summary>
internal sealed class ExternalEditConflictException : InvalidOperationException
{
    public ExternalEditConflictException()
    {
    }

    public ExternalEditConflictException(string message) : base(message)
    {
    }

    public ExternalEditConflictException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
