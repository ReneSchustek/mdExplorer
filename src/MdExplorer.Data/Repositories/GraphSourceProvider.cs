using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Repositories;

/// <summary>
/// EF-Core-Implementierung von <see cref="IGraphSourceProvider"/>. Lädt alle Dateien
/// und Dokumente in zwei untracked Queries — der Snapshot ist ein Read-Only-View.
/// </summary>
public sealed class GraphSourceProvider : IGraphSourceProvider
{
    private readonly MdExplorerDbContext _dbContext;

    /// <summary>Erzeugt den Provider und injiziert den DbContext.</summary>
    public GraphSourceProvider(MdExplorerDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<GraphSourceData> LoadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<GraphSourceFile> files = await LoadFilesAsync(cancellationToken).ConfigureAwait(false);

        List<GraphSourceDocument> documents = await _dbContext.Set<MarkdownDocument>()
            .AsNoTracking()
            .OrderBy(document => document.MarkdownFileId)
            .Select(document => new GraphSourceDocument(document.MarkdownFileId, document.OutlinksJson))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new GraphSourceData(files, documents);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphSourceFile>> LoadFilesAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Set<MarkdownFile>()
            .AsNoTracking()
            .OrderBy(file => file.Id)
            .Select(file => new GraphSourceFile(file.Id, file.FileNameWithoutExtension, file.RelativePath))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphSourceDocument>> LoadNeighborhoodDocumentsAsync(
        Guid markdownFileId,
        string targetSlug,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSlug);

        // Die Verweis-Liste ist ein JSON-Array aus Zeichenketten; ein Ziel steht dort in
        // Anführungszeichen. Danach wird gesucht — ein Vorfilter, den der Aufrufer beim
        // Auswerten der Liste ohnehin bestätigt oder verwirft.
        string needle = "%\"" + targetSlug + "\"%";

        return await _dbContext.Set<MarkdownDocument>()
            .AsNoTracking()
            .Where(document => document.MarkdownFileId == markdownFileId
                || EF.Functions.Like(document.OutlinksJson, needle))
            .OrderBy(document => document.MarkdownFileId)
            .Select(document => new GraphSourceDocument(document.MarkdownFileId, document.OutlinksJson))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
