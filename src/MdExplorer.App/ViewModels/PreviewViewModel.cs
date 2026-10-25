using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
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

    /// <summary>Der zuletzt angezeigte Inhalt, Grundlage für einen Neuaufbau.</summary>
    private string _body = string.Empty;

    [ObservableProperty]
    private string _html;

    [ObservableProperty]
    private Guid? _currentDocumentId;

    /// <summary>
    /// Wogegen die relativen Bildpfade des angezeigten Dokuments aufgelöst werden, oder
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Die Ansicht bildet <see cref="PreviewImageBase.Folder"/> auf den virtuellen Ordner der
    /// Vorschau ab. Ohne ihn bleibt jedes Bild einer Notiz leer, denn ein relativer Pfad hat in
    /// einem per Zeichenkette geladenen Dokument keine Basis, auf die er sich beziehen könnte.
    /// </remarks>
    [ObservableProperty]
    private PreviewImageBase? _imageBase;

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
        // Auch die leere Vorschau trägt die Farben des Erscheinungsbilds. Ein blankes
        // HTML-Gerüst wäre im Dunkeln eine weiße Fläche über die halbe Anwendung.
        _html = htmlBuilder.BuildEmpty();
    }

    /// <summary>
    /// Baut die Anzeige mit den geltenden Einstellungen neu auf.
    /// </summary>
    /// <remarks>
    /// Ein Wechsel von Erscheinungsbild oder Schriftgröße tauscht die Wörterbücher der
    /// Oberfläche; die Vorschau ist aber ein fertiges HTML-Dokument und bliebe sonst im alten
    /// Stand stehen, bis das Dokument erneut geladen wird.
    /// </remarks>
    public void RebuildForSettings()
    {
        Html = Compose();
    }

    /// <summary>
    /// Zeigt frisch gerendertes HTML zum selben Dokument. Das <see cref="DocumentPanelViewModel"/>
    /// ruft es nach dem Speichern, damit die Vorschau den gespeicherten Stand zeigt, ohne auf den
    /// Indexer zu warten.
    /// </summary>
    /// <remarks>
    /// Der Inhalt wird gemerkt wie beim Laden. <see cref="RebuildForSettings"/> baut aus ihm neu auf;
    /// bliebe dort der geladene Stand, spränge die Vorschau beim nächsten Wechsel des
    /// Erscheinungsbilds auf den Text vor dem Speichern zurück.
    /// </remarks>
    /// <param name="renderedHtmlGz">Das gerenderte HTML, gzip-komprimiert wie vom Parser geliefert.</param>
    public void ShowRendered(ReadOnlyMemory<byte> renderedHtmlGz)
    {
        _body = DecompressHtml(renderedHtmlGz);
        Html = Compose();
    }

    /// <summary>
    /// Zeigt eine Datei, die der Indexer noch nicht erfasst hat und die deshalb keine Kennung hat.
    /// </summary>
    /// <param name="renderedHtmlGz">Das gerenderte HTML, gzip-komprimiert wie vom Parser geliefert.</param>
    /// <param name="absolutePath">Pfad der Datei; ihr Ordner ist die Basis für relative Bilder.</param>
    public void ShowUnindexedFile(ReadOnlyMemory<byte> renderedHtmlGz, string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        // Erst Ordner und Kennung, dann das HTML: Die Ansicht richtet beim Wechsel von Html den
        // virtuellen Ordner ein. Ohne neuen Ordner zeigte sie die Bilder des vorigen Dokuments.
        // Der Index kennt die Datei nicht; freigegeben wird deshalb nur ihr eigener Ordner.
        ImageBase = PreviewImageBase.ForFile(absolutePath, relativePath: null);
        CurrentDocumentId = null;
        ShowRendered(renderedHtmlGz);
    }

    /// <summary>Zeigt die leere Vorschau; es ist kein Dokument geöffnet.</summary>
    public void Clear()
    {
        _body = string.Empty;
        ImageBase = null;
        Html = _htmlBuilder.BuildEmpty();
        CurrentDocumentId = null;
    }

    /// <summary>Lädt das Dokument und aktualisiert <see cref="Html"/>.</summary>
    /// <remarks>
    /// Nach jedem Warten wird der Merker geprüft: Ein überholter Ladevorgang darf die Anzeige
    /// des neueren nicht mehr überschreiben.
    /// </remarks>
    public async Task LoadAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        if (markdownFileId == Guid.Empty)
        {
            Clear();
            return;
        }

        try
        {
            MarkdownDocument? document = await LoadDocumentAsync(markdownFileId, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            await ShowAsync(markdownFileId, document, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Ein neuerer Klick hat übernommen; die Anzeige bleibt, wie sie ist.
        }
        catch (Exception exception) when (DataLoadFailure.IsExpected(exception))
        {
            // Datenbank-Spitze beim Nachschlagen: Die Datei gilt als angezeigt, nur leer. Sonst
            // lüde die Oberfläche dieselbe Datei endlos nach.
            LogPreviewLoadFailed(_logger, markdownFileId, exception);
            ShowEmptyFor(markdownFileId);
        }
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

    [LoggerMessage(EventId = 312, Level = LogLevel.Warning, Message = "Bildpfade der Vorschau nicht umgeschrieben, die Prüfung brauchte zu lange; das Dokument erscheint ohne relative Bilder.")]
    private static partial void LogImageRewriteTimedOut(ILogger logger, Exception exception);

    /// <summary>Baut das Dokument aus dem gemerkten Inhalt und der Bildbasis.</summary>
    /// <remarks>
    /// Das Umschreiben der Bildpfade hat eine Zeitgrenze gegen kaputtes HTML. Reißt sie, erscheint
    /// das Dokument ohne relative Bilder statt gar nicht; ohne Basis wird nichts umgeschrieben.
    /// </remarks>
    private string Compose()
    {
        try
        {
            return _htmlBuilder.Build(_body, ImageBase);
        }
        catch (RegexMatchTimeoutException exception)
        {
            LogImageRewriteTimedOut(_logger, exception);
            return _htmlBuilder.Build(_body, imageBase: null);
        }
    }

    private async Task ShowAsync(Guid markdownFileId, MarkdownDocument? document, CancellationToken cancellationToken)
    {
        if (document is null)
        {
            LogDocumentMissing(_logger, markdownFileId);
            ShowEmptyFor(markdownFileId);
            return;
        }

        string body = DecompressHtml(document.RenderedHtmlGz);
        PreviewImageBase? imageBase = await LoadImageBaseAsync(markdownFileId, cancellationToken).ConfigureAwait(true);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _body = body;
        // Erst der Ordner, dann das HTML: Die Ansicht hängt am Wechsel von Html und richtet
        // den virtuellen Ordner vor dem Anzeigen ein.
        ImageBase = imageBase;
        Html = Compose();
        CurrentDocumentId = markdownFileId;
    }

    private void ShowEmptyFor(Guid markdownFileId)
    {
        _body = string.Empty;
        Html = _htmlBuilder.BuildEmpty();
        CurrentDocumentId = markdownFileId;
    }

    /// <summary>
    /// Liest den Ort der Datei im Index. Ein Fehlschlag kostet die Bilder, nicht die Vorschau.
    /// </summary>
    /// <remarks>
    /// Bewusst <c>GetService</c> statt <c>GetRequiredService</c>: Der Ort ist für relative
    /// Bildpfade da, nicht fürs Anzeigen. Wer ihn nicht auflösen kann, soll ein Dokument ohne
    /// Bilder sehen und nicht eine leere Fläche.
    /// </remarks>
    private async Task<PreviewImageBase?> LoadImageBaseAsync(Guid markdownFileId, CancellationToken cancellationToken)
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
                return file is null ? null : PreviewImageBase.ForFile(file.AbsolutePath, file.RelativePath);
            }
        }
        catch (Exception exception) when (DataLoadFailure.IsExpected(exception))
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
