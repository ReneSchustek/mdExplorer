using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.App.Tests.Services;

/// <summary>Tests für den <see cref="MarkdownFileDocumentLocator"/> — WikiLink-Auflösung.</summary>
public sealed class NavigationAndLocatorTests
{
    [Fact]
    public async Task FindByWikiLinkAsync_MatchesByFileNameCaseInsensitive()
    {
        FakeMarkdownFileRepository repo = new();
        Guid expected = Guid.NewGuid();
        repo.Add(new MarkdownFile
        {
            Id = expected,
            AbsolutePath = @"C:\notes\Projekt-X.md",
            RelativePath = "Projekt-X.md",
            FileNameWithoutExtension = "Projekt-X",
            ContentHash = "h",
        });

        using ServiceProvider provider = BuildProvider(repo);
        MarkdownFileDocumentLocator locator = new(provider.GetRequiredService<IServiceScopeFactory>());

        Guid? result = await locator.FindByWikiLinkAsync("projekt-x", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task FindByWikiLinkAsync_UnknownTargetReturnsNull()
    {
        FakeMarkdownFileRepository repo = new();
        using ServiceProvider provider = BuildProvider(repo);
        MarkdownFileDocumentLocator locator = new(provider.GetRequiredService<IServiceScopeFactory>());

        Guid? result = await locator.FindByWikiLinkAsync("unbekannt", CancellationToken.None).ConfigureAwait(true);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindByAbsolutePathAsync_ResolvesIndexedFileToId()
    {
        FakeMarkdownFileRepository repo = new();
        Guid expected = Guid.NewGuid();
        repo.Add(new MarkdownFile
        {
            Id = expected,
            AbsolutePath = @"C:\notes\Pfad.md",
            RelativePath = "Pfad.md",
            FileNameWithoutExtension = "Pfad",
            ContentHash = "h",
        });

        using ServiceProvider provider = BuildProvider(repo);
        MarkdownFileDocumentLocator locator = new(provider.GetRequiredService<IServiceScopeFactory>());

        Guid? result = await locator.FindByAbsolutePathAsync(@"C:\notes\Pfad.md", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task FindByAbsolutePathAsync_UnknownPathReturnsNull()
    {
        FakeMarkdownFileRepository repo = new();
        using ServiceProvider provider = BuildProvider(repo);
        MarkdownFileDocumentLocator locator = new(provider.GetRequiredService<IServiceScopeFactory>());

        Guid? result = await locator.FindByAbsolutePathAsync(@"C:\notes\nicht-indiziert.md", CancellationToken.None).ConfigureAwait(true);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindByWikiLinkAsync_ResolvesRepositoryPerCall()
    {
        FakeMarkdownFileRepository repo = new();
        using ServiceProvider provider = BuildProvider(repo);
        MarkdownFileDocumentLocator locator = new(provider.GetRequiredService<IServiceScopeFactory>());

        Guid? first = await locator.FindByWikiLinkAsync("erst-unbekannt", CancellationToken.None).ConfigureAwait(true);
        Assert.Null(first);

        Guid expected = Guid.NewGuid();
        repo.Add(new MarkdownFile
        {
            Id = expected,
            AbsolutePath = @"C:\notes\Spaeter.md",
            RelativePath = "Spaeter.md",
            FileNameWithoutExtension = "Spaeter",
            ContentHash = "h",
        });

        Guid? second = await locator.FindByWikiLinkAsync("spaeter", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(expected, second);
    }

    private static ServiceProvider BuildProvider(FakeMarkdownFileRepository repository)
    {
        ServiceCollection services = new();
        _ = services.AddScoped<IMarkdownFileRepository>(_ => repository);
        return services.BuildServiceProvider(validateScopes: true);
    }
}
