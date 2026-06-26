using System.Runtime.CompilerServices;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Repositories;

/// <summary>EF-Core-gestützte Implementierung von <see cref="IMarkdownSourceProvider"/> über <see cref="MarkdownFile"/>.</summary>
public sealed class MarkdownSourceProvider(MdExplorerDbContext dbContext) : IMarkdownSourceProvider
{
    private readonly MdExplorerDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    /// <inheritdoc />
    public async IAsyncEnumerable<MarkdownSourceSnapshot> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerable<MarkdownSourceSnapshot> query = _dbContext.Set<MarkdownFile>()
            .AsNoTracking()
            .OrderBy(file => file.Id)
            .Select(file => new MarkdownSourceSnapshot(file.Id, file.AbsolutePath, file.ContentHash))
            .AsAsyncEnumerable();

        await foreach (MarkdownSourceSnapshot snapshot in query.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return snapshot;
        }
    }
}
