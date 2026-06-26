using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>In-Memory-Repo für Preview-Tests.</summary>
internal sealed class FakeMarkdownDocumentRepository : IMarkdownDocumentRepository
{
    private readonly Dictionary<Guid, MarkdownDocument> _byFileId = [];

    public void Put(Guid markdownFileId, MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _byFileId[markdownFileId] = document;
    }

    public Task<MarkdownDocument?> GetByMarkdownFileIdAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
        Task.FromResult(_byFileId.TryGetValue(markdownFileId, out MarkdownDocument? doc) ? doc : null);

    public Task<IReadOnlyList<Guid>> GetStaleOrMissingAsync(IReadOnlyDictionary<Guid, string> hashesByFileId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task AddAsync(MarkdownDocument document, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public void Update(MarkdownDocument document) { }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
}
