using System.Data.Common;
using System.IO;
using System.IO.Compression;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Container-ViewModel der rechten Spalte. Verwaltet den Mode-Switch zwischen
/// <see cref="DocumentPanelMode.Read"/> (WebView2-Vorschau) und <see cref="DocumentPanelMode.Edit"/>
/// (Markdown-Editor) und uebernimmt nach dem Speichern die direkte Vorschau-Aktualisierung
/// gegen den aktuellen Editor-Text — damit Akzeptanzkriterium 4 (Preview &lt;= 2 s nach Save)
/// unabhaengig vom Hintergrund-Indexer eingehalten wird.
/// </summary>
internal sealed partial class DocumentPanelViewModel : ObservableObject, IDisposable
{
    private readonly IMarkdownParser _markdownParser;
    private readonly PreviewHtmlBuilder _htmlBuilder;
    private readonly IDocumentLocator _documentLocator;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<DocumentPanelViewModel> _logger;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadMode))]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    private DocumentPanelMode _mode = DocumentPanelMode.Read;

    /// <summary>Erstellt das ViewModel und verdrahtet die Save-Event-Bruecke zur Preview.</summary>
    public DocumentPanelViewModel(
        PreviewViewModel preview,
        MarkdownEditorViewModel editor,
        IMarkdownParser markdownParser,
        PreviewHtmlBuilder htmlBuilder,
        IDocumentLocator documentLocator,
        IFileSystem fileSystem,
        ILogger<DocumentPanelViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(markdownParser);
        ArgumentNullException.ThrowIfNull(htmlBuilder);
        ArgumentNullException.ThrowIfNull(documentLocator);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);

        Preview = preview;
        Editor = editor;
        _markdownParser = markdownParser;
        _htmlBuilder = htmlBuilder;
        _documentLocator = documentLocator;
        _fileSystem = fileSystem;
        _logger = logger;

        Editor.Saved += OnEditorSaved;
    }

    /// <summary>Liefert die Preview im Read-Modus.</summary>
    public PreviewViewModel Preview { get; }

    /// <summary>Liefert den Editor im Edit-Modus.</summary>
    public MarkdownEditorViewModel Editor { get; }

    /// <summary>Wahr, solange das Panel im Lese-Modus ist (Binding-Helfer fuer Visibility).</summary>
    public bool IsReadMode => Mode == DocumentPanelMode.Read;

    /// <summary>Wahr, solange das Panel im Bearbeiten-Modus ist (Binding-Helfer fuer Visibility).</summary>
    public bool IsEditMode => Mode == DocumentPanelMode.Edit;

    /// <summary>
    /// Laedt das Dokument in beiden Modi. Auswahl-Pfad wird ueber den
    /// <see cref="IDocumentLocator"/> aufgeloest, damit der Aufruf nur die Id kennen muss.
    /// </summary>
    public async Task LoadAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        try
        {
            await Preview.LoadAsync(markdownFileId, cancellationToken).ConfigureAwait(true);

            string? absolutePath = await _documentLocator.GetAbsolutePathAsync(markdownFileId, cancellationToken).ConfigureAwait(true);
            if (absolutePath is null)
            {
                LogPathMissing(_logger, markdownFileId);
                return;
            }
            await Editor.LoadAsync(markdownFileId, absolutePath, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Load wurde abgebrochen — kein Fehler.
        }
        catch (DbException exception)
        {
            // SQLite-Spitze beim Document-Lookup — Panel bleibt leer.
            LogLoadFailed(_logger, markdownFileId, exception);
        }
    }

    /// <summary>
    /// Laedt eine Datei direkt vom Dateisystem — Fallback fuer Klicks auf Markdown-Dateien,
    /// die der Indexer noch nicht erfasst hat. Wenn die Datei bereits in der DB liegt,
    /// wird der ueberlieferte ID-Pfad bevorzugt (Tag-Persistenz, Outlink-Resolution).
    /// </summary>
    public async Task LoadByPathAsync(string absolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        Guid? indexedId = await _documentLocator
            .FindByAbsolutePathAsync(absolutePath, cancellationToken)
            .ConfigureAwait(true);
        if (indexedId is { } id)
        {
            await LoadAsync(id, cancellationToken).ConfigureAwait(true);
            return;
        }

        await LoadDirectFromFileAsync(absolutePath, cancellationToken).ConfigureAwait(true);
    }

    private async Task LoadDirectFromFileAsync(string absolutePath, CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(absolutePath))
        {
            LogDirectLoadMissingFile(_logger, absolutePath);
            return;
        }

        byte[] bytes;
        try
        {
            bytes = await _fileSystem
                .ReadAllBytesAsync(absolutePath, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogDirectLoadFailed(_logger, absolutePath, ex);
            return;
        }

        string text = Encoding.UTF8.GetString(bytes);
        ParseResult result = _markdownParser.Parse(text);
        string body = DecompressHtml(result.RenderedHtmlGz);
        Preview.SetHtml(_htmlBuilder.Build(body));
        // Editor uebernimmt den Rohtext via LoadDirectAsync. Eine kuenstliche Guid waere
        // im Conflict-Check unsicher, deshalb laeuft der Editor in einem Read-Only-Modus
        // ohne MarkdownFileId — Save bleibt gesperrt, bis der Indexer die Datei kennt.
        await Editor.LoadDirectAsync(absolutePath, text, cancellationToken).ConfigureAwait(true);
        LogDirectLoadCompleted(_logger, absolutePath);
    }

    /// <summary>Wechselt zwischen Lese- und Bearbeiten-Modus (Toolbar-Button / Ctrl+E).</summary>
    [RelayCommand]
    public void ToggleMode()
    {
        Mode = Mode == DocumentPanelMode.Read ? DocumentPanelMode.Edit : DocumentPanelMode.Read;
    }

    /// <summary>Speichert den Editor — wird vom MainWindow ueber Ctrl+S aufgerufen.</summary>
    [RelayCommand]
    public Task SaveAsync(CancellationToken cancellationToken) => Editor.SaveAsync(cancellationToken);

    /// <summary>Loest Event-Handler und gibt Ressourcen frei.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Editor.Saved -= OnEditorSaved;
        Editor.Dispose();
    }

    private void OnEditorSaved(object? sender, EditorSavedEventArgs args)
    {
        try
        {
            ParseResult result = _markdownParser.Parse(args.SavedText);
            string body = DecompressHtml(result.RenderedHtmlGz);
            Preview.SetHtml(_htmlBuilder.Build(body));
        }
        catch (InvalidOperationException exception)
        {
            LogPreviewRefreshFailed(_logger, exception);
        }
    }

    private static string DecompressHtml(ReadOnlyMemory<byte> compressed)
    {
        if (compressed.IsEmpty)
        {
            return string.Empty;
        }
        byte[] buffer = compressed.ToArray();
        using MemoryStream input = new(buffer, writable: false);
        using GZipStream gzip = new(input, CompressionMode.Decompress);
        using StreamReader reader = new(gzip, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [LoggerMessage(EventId = 510, Level = LogLevel.Warning, Message = "Editor-Load fuer {MarkdownFileId} ohne Datei-Pfad — Indexer-Eintrag fehlt.")]
    private static partial void LogPathMissing(ILogger logger, Guid markdownFileId);

    [LoggerMessage(EventId = 511, Level = LogLevel.Error, Message = "Preview-Refresh nach Save fehlgeschlagen.")]
    private static partial void LogPreviewRefreshFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 512, Level = LogLevel.Information, Message = "Datei {Path} direkt aus dem Dateisystem geladen (Indexer noch nicht durch).")]
    private static partial void LogDirectLoadCompleted(ILogger logger, string path);

    [LoggerMessage(EventId = 513, Level = LogLevel.Warning, Message = "Direkt-Load fuer {Path} fehlgeschlagen.")]
    private static partial void LogDirectLoadFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 514, Level = LogLevel.Warning, Message = "Direkt-Load uebersprungen — Datei {Path} existiert nicht.")]
    private static partial void LogDirectLoadMissingFile(ILogger logger, string path);

    [LoggerMessage(EventId = 515, Level = LogLevel.Warning, Message = "Document-Load fuer {MarkdownFileId} fehlgeschlagen — Datenbank-Spitze.")]
    private static partial void LogLoadFailed(ILogger logger, Guid markdownFileId, Exception exception);
}
