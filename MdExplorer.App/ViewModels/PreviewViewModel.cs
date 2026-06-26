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
    private const string EmptyPreviewHtml = "<!doctype html><html><body></body></html>";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PreviewHtmlBuilder _htmlBuilder;
    private readonly ILogger<PreviewViewModel> _logger;

    [ObservableProperty]
    private string _html = EmptyPreviewHtml;

    [ObservableProperty]
    private Guid? _currentDocumentId;

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
            Html = EmptyPreviewHtml;
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
            Html = _htmlBuilder.BuildEmpty();
            CurrentDocumentId = markdownFileId;
            return;
        }

        if (document is null)
        {
            LogDocumentMissing(_logger, markdownFileId);
            Html = _htmlBuilder.BuildEmpty();
            CurrentDocumentId = markdownFileId;
            return;
        }

        string body = DecompressHtml(document.RenderedHtmlGz);
        Html = _htmlBuilder.Build(body);
        CurrentDocumentId = markdownFileId;
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
}
