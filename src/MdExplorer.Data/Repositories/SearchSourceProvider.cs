using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Repositories;

/// <summary>
/// EF-Core-Implementierung von <see cref="ISearchSourceProvider"/>. Lädt Dokument-Stammdaten
/// und Tag-Aggregat in zwei untracked Queries, denn der Snapshot ist ein Read-Only-View.
/// </summary>
public sealed class SearchSourceProvider : ISearchSourceProvider
{
    private readonly MdExplorerDbContext _dbContext;

    /// <summary>Erzeugt den Provider und injiziert den DbContext.</summary>
    public SearchSourceProvider(MdExplorerDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<SearchSourceData> LoadAsync(CancellationToken cancellationToken)
    {
        List<SearchSourceDocument> documents = await LoadDocumentsAsync(cancellationToken).ConfigureAwait(false);
        List<TagJoinRow> tagRows = await _dbContext.Set<MarkdownFileTag>()
            .AsNoTracking()
            .Join(
                _dbContext.Set<Tag>().AsNoTracking(),
                link => link.TagId,
                tag => tag.Id,
                (link, tag) => new TagJoinRow(link.MarkdownFileId, tag.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new SearchSourceData(documents, TagsByFile.Group(tagRows.Select(row => (row.MarkdownFileId, row.Name))));
    }

    /// <inheritdoc />
    public async Task<SearchSourceVersion> LoadVersionAsync(CancellationToken cancellationToken)
    {
        IQueryable<MarkdownDocument> documents = _dbContext.Set<MarkdownDocument>().AsNoTracking();
        IQueryable<MarkdownFile> files = _dbContext.Set<MarkdownFile>().AsNoTracking();
        return new SearchSourceVersion(
            await documents.CountAsync(cancellationToken).ConfigureAwait(false),
            await documents.MaxAsync(document => (DateTime?)document.ParsedAtUtc, cancellationToken).ConfigureAwait(false),
            await files.CountAsync(cancellationToken).ConfigureAwait(false),
            await files.MaxAsync(file => (DateTime?)file.IndexedAtUtc, cancellationToken).ConfigureAwait(false));
    }

    // Feste Reihenfolge: Der Abgleich des Suchindex schneidet nach einer Stapelgröße ab, und
    // ohne Reihenfolge wählte SQLite bei jedem Takt einen anderen Stapel. Sortiert wird in der
    // Abfrage über den Primärschlüssel, nicht hinterher im Speicher.
    private Task<List<SearchSourceDocument>> LoadDocumentsAsync(CancellationToken cancellationToken) =>
        _dbContext.Set<MarkdownDocument>()
            .AsNoTracking()
            .Join(
                _dbContext.Set<MarkdownFile>().AsNoTracking(),
                document => document.MarkdownFileId,
                file => file.Id,
                (document, file) => new { Document = document, File = file })
            .OrderBy(pair => pair.Document.MarkdownFileId)
            .Select(pair => new SearchSourceDocument(
                pair.Document.MarkdownFileId,
                pair.File.FileNameWithoutExtension,
                pair.File.AbsolutePath,
                pair.File.RelativePath,
                pair.Document.SourceContentHash,
                pair.Document.FrontmatterJson))
            .ToListAsync(cancellationToken);

    /// <summary>Eine Zuordnung von Datei und Schlagwort mit dem Originalnamen, der in die Tags-Spalte geht.</summary>
    private sealed record TagJoinRow(Guid MarkdownFileId, string Name);
}
