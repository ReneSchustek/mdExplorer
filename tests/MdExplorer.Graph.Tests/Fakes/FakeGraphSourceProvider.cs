using MdExplorer.Core.Abstractions;

namespace MdExplorer.Graph.Tests.Fakes;

/// <summary>
/// Quellenanbieter über einem festen Datensatz.
/// </summary>
/// <remarks>
/// Der Vorfilter für die Nachbarschaft bildet die Textsuche der Datenschicht nach: Er sucht
/// den Slug in Anführungszeichen in der Verweis-Liste. Damit prüfen die Tests dasselbe
/// Verhalten, das die Datenbank liefert — einschließlich der Fehltreffer, die der Aufrufer
/// beim Auswerten verwerfen muss.
/// </remarks>
internal sealed class FakeGraphSourceProvider(GraphSourceData data) : IGraphSourceProvider
{
    /// <summary>Wie oft der vollständige Schnappschuss angefordert wurde.</summary>
    public int FullLoadCount { get; private set; }

    /// <summary>Wie oft nur die Nachbarschaft angefordert wurde.</summary>
    public int NeighborhoodLoadCount { get; private set; }

    /// <summary>Wie viele Verweis-Listen der letzte Nachbarschafts-Aufruf geliefert hat.</summary>
    public int LastNeighborhoodSize { get; private set; }

    /// <inheritdoc />
    public Task<GraphSourceData> LoadAsync(CancellationToken cancellationToken)
    {
        FullLoadCount++;

        return Task.FromResult(data);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphSourceFile>> LoadFilesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(data.Files);

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphSourceDocument>> LoadNeighborhoodDocumentsAsync(
        Guid markdownFileId,
        string targetSlug,
        CancellationToken cancellationToken)
    {
        NeighborhoodLoadCount++;
        string needle = "\"" + targetSlug + "\"";

        IReadOnlyList<GraphSourceDocument> hits =
        [
            .. data.Documents.Where(document =>
                document.MarkdownFileId == markdownFileId
                || document.OutlinksJson.Contains(needle, StringComparison.Ordinal))
        ];

        LastNeighborhoodSize = hits.Count;

        return Task.FromResult(hits);
    }
}
