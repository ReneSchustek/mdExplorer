using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.App.Services;

/// <summary>
/// Standard-Implementierung von <see cref="IDocumentLocator"/>. Öffnet pro Aufruf einen eigenen
/// DI-Scope und delegiert an <see cref="IMarkdownFileRepository"/> — vermeidet das Captive-Dependency-
/// Antipattern (Singleton hält Scoped → DbContext lebt ewig).
/// </summary>
internal sealed class MarkdownFileDocumentLocator : IDocumentLocator
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Erzeugt den Locator. <paramref name="scopeFactory"/> erzeugt pro Aufruf einen Scope.</summary>
    public MarkdownFileDocumentLocator(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task<Guid?> FindByWikiLinkAsync(string wikiLinkTarget, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wikiLinkTarget);

        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IMarkdownFileRepository repository = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
            return await repository.FindIdByFileNameAsync(wikiLinkTarget, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<Guid?> FindByAbsolutePathAsync(string absoluteFilePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);

        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IMarkdownFileRepository repository = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
            Core.Models.MarkdownFile? file = await repository.GetByAbsolutePathAsync(absoluteFilePath, cancellationToken).ConfigureAwait(false);
            return file?.Id;
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetAbsolutePathAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        if (markdownFileId == Guid.Empty)
        {
            return null;
        }
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IMarkdownFileRepository repository = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
            Core.Models.MarkdownFile? file = await repository.GetByIdAsync(markdownFileId, cancellationToken).ConfigureAwait(false);
            return file?.AbsolutePath;
        }
    }
}
