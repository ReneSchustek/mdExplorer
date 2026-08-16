using System.Data.Common;
using System.IO;
using System.IO.Compression;
using CommunityToolkit.Mvvm.ComponentModel;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// ViewModel der rechten Spalte. Lädt das gerenderte HTML zur ausgewählten Markdown-Datei,
/// dekomprimiert es und reicht es an den <see cref="PreviewHtmlBuilder"/> weiter, der das
/// vollständige HTML-Dokument inkl. CSP und Theme erzeugt. Der Zugriff auf den Scoped
/// <see cref="IMarkdownDocumentRepository"/> erfolgt pro Ladevorgang über einen eigenen DI-Scope,
/// damit das Singleton-ViewModel kein Captive-DbContext-Antipattern erzeugt.
/// </summary>
internal sealed partial class PreviewViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PreviewHtmlBuilder _htmlBuilder;
    private readonly ILogger<PreviewViewModel> _logger;

    /// <summary>Der zuletzt angezeigte Inhalt — Grundlage für einen Neuaufbau.</summary>
    private string _body = string.Empty;

    [ObservableProperty]
    private string _html;

    [ObservableProperty]
    private Guid? _currentDocumentId;

    /// <summary>
    /// Der Ordner, in dem das angezeigte Dokument liegt — oder <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Die Ansicht bildet ihn auf den virtuellen Ordner der Vorschau ab. Ohne ihn bleibt
    /// jedes Bild einer Notiz leer, denn ein relativer Pfad hat in einem per Zeichenkette
    /// geladenen Dokument keine Basis, auf die er sich beziehen könnte.
    /// </remarks>
    [ObservableProperty]
    private string? _documentFolder;

    /// <summary>Erzeugt das ViewModel.</summary>
    public PreviewViewModel(
        IServiceScopeFactory scopeFactory,
        PreviewHtmlBuilder htmlBuilder,
        ILogger<PreviewViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(htmlBuilder);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _htmlBuilder = htmlBuilder;
        _logger = logger;
        // Auch die leere Vorschau trägt die Farben des Erscheinungsbilds. Vorher stand hier
        // ein blankes HTML-Gerüst — im Dunkeln eine weiße Fläche über die halbe Anwendung.
        _html = htmlBuilder.BuildEmpty();
    }

    /// <summary>
    /// Baut die Anzeige mit der geltenden Belegung neu auf.
    /// </summary>
    /// <remarks>
    /// Der Wechsel des Erscheinungsbilds tauscht die Wörterbücher der Oberfläche; die
    /// Vorschau ist aber ein fertiges HTML-Dokument und bliebe sonst in der alten Belegung
    /// stehen, bis das Dokument erneut geladen wird.
    /// </remarks>
    public void RebuildForTheme()
    {
        Html = _htmlBuilder.Build(_body);
    }

    /// <summary>
    /// Setzt das HTML direkt — wird vom <see cref="DocumentPanelViewModel"/> nach einem Save aufgerufen,
    /// damit die Preview innerhalb der 2-s-Schwelle aktualisiert ist, ohne auf den Hintergrund-Indexer zu warten.
    /// </summary>
    public void SetHtml(string fullHtml)
    {
        ArgumentNullException.ThrowIfNull(fullHtml);
        Html = fullHtml;
    }

    /// <summary>Lädt das Dokument und aktualisiert <see cref="Html"/>.</summary>
    public async Task LoadAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        if (markdownFileId == Guid.Empty)
        {
            _body = string.Empty;
            Html = _htmlBuilder.BuildEmpty();
            CurrentDocumentId = null;
            return;
        }

        MarkdownDocument? document;
        try
        {
            document = await LoadDocumentAsync(markdownFileId, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (DbException exception)
        {
            // SQLite-Spitze beim Preview-Lookup — leeres Dokument anzeigen.
            LogPreviewLoadFailed(_logger, markdownFileId, exception);
            _body = string.Empty;
            Html = _htmlBuilder.BuildEmpty();
            CurrentDocumentId = markdownFileId;
            return;
        }

        if (document is null)
        {
            LogDocumentMissing(_logger, markdownFileId);
            _body = string.Empty;
            Html = _htmlBuilder.BuildEmpty();
            CurrentDocumentId = markdownFileId;
            return;
        }

        _body = DecompressHtml(document.RenderedHtmlGz);
        // Erst der Ordner, dann das HTML: Die Ansicht hängt am Wechsel von Html und richtet
        // den virtuellen Ordner vor dem Anzeigen ein.
        DocumentFolder = await LoadDocumentFolderAsync(markdownFileId, cancellationToken).ConfigureAwait(true);
        Html = _htmlBuilder.Build(_body);
        CurrentDocumentId = markdownFileId;
    }

    private static string DecompressHtml(ReadOnlyMemory<byte> compressed)
    {
        if (compressed.IsEmpty)
        {
            return string.Empty;
        }
        byte[] buffer = compressed.ToArray();
        using MemoryStream input = new(buffer, 0, buffer.Length, writable: false);
        using GZipStream gzip = new(input, CompressionMode.Decompress);
        using StreamReader reader = new(gzip, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [LoggerMessage(EventId = 310, Level = LogLevel.Warning, Message = "Kein Markdown-Dokument für Datei {MarkdownFileId} im Parser-Store.")]
    private static partial void LogDocumentMissing(ILogger logger, Guid markdownFileId);

    [LoggerMessage(EventId = 311, Level = LogLevel.Warning, Message = "Preview-Lookup für Datei {MarkdownFileId} fehlgeschlagen — Datenbank-Spitze.")]
    private static partial void LogPreviewLoadFailed(ILogger logger, Guid markdownFileId, Exception exception);

    /// <summary>
    /// Liest den Ordner der Datei — ein Fehlschlag kostet die Bilder, nicht die Vorschau.
    /// </summary>
    /// <remarks>
    /// Bewusst <c>GetService</c> statt <c>GetRequiredService</c>: Der Ordner ist für relative
    /// Bildpfade da, nicht fürs Anzeigen. Wer ihn nicht auflösen kann, soll ein Dokument ohne
    /// Bilder sehen und nicht eine leere Fläche.
    /// </remarks>
    private async Task<string?> LoadDocumentFolderAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        try
        {
            AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(true))
            {
                IMarkdownFileRepository? repository = scope.ServiceProvider.GetService<IMarkdownFileRepository>();
                if (repository is null)
                {
                    return null;
                }
                MarkdownFile? file = await repository.GetByIdAsync(markdownFileId, cancellationToken).ConfigureAwait(true);
                return file is null ? null : Path.GetDirectoryName(file.AbsolutePath);
            }
        }
        catch (DbException exception)
        {
            LogPreviewLoadFailed(_logger, markdownFileId, exception);
            return null;
        }
    }

    private async Task<MarkdownDocument?> LoadDocumentAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(true))
        {
            IMarkdownDocumentRepository repository = scope.ServiceProvider.GetRequiredService<IMarkdownDocumentRepository>();
            return await repository.GetByMarkdownFileIdAsync(markdownFileId, cancellationToken).ConfigureAwait(true);
        }
    }
}
