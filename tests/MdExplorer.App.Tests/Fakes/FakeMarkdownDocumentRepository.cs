using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>In-Memory-Repo für Preview-Tests.</summary>
internal sealed class FakeMarkdownDocumentRepository : IMarkdownDocumentRepository
{
    private readonly Dictionary<Guid, MarkdownDocument> _byFileId = [];

    /// <summary>
    /// Fehler, den <see cref="GetByMarkdownFileIdAsync"/> statt eines Ergebnisses liefert.
    /// Nötig, um die Ausweichpfade der Vorschau zu prüfen — eine Datenbank-Spitze lässt sich
    /// mit einem In-Memory-Bestand sonst nicht nachstellen.
    /// </summary>
    public Exception? FailOnGet { get; set; }

    public void Put(Guid markdownFileId, MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _byFileId[markdownFileId] = document;
    }

    public Task<MarkdownDocument?> GetByMarkdownFileIdAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
        FailOnGet is not null
            ? Task.FromException<MarkdownDocument?>(FailOnGet)
            : Task.FromResult(_byFileId.TryGetValue(markdownFileId, out MarkdownDocument? doc) ? doc : null);

    public Task<IReadOnlyDictionary<Guid, MarkdownDocument>> GetByMarkdownFileIdsAsync(
        IReadOnlyCollection<Guid> markdownFileIds,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, MarkdownDocument> result = [];
        foreach (Guid id in markdownFileIds)
        {
            if (_byFileId.TryGetValue(id, out MarkdownDocument? doc))
            {
                result[id] = doc;
            }
        }
        return Task.FromResult<IReadOnlyDictionary<Guid, MarkdownDocument>>(result);
    }

    public Task<IReadOnlyList<Guid>> GetStaleOrMissingAsync(IReadOnlyDictionary<Guid, string> hashesByFileId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task AddAsync(MarkdownDocument document, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public void Update(MarkdownDocument document) { }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
}
