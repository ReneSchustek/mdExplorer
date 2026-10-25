using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Graph.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Ein WikiLink findet seine Datei über dieselbe Regel wie die Kante im Graphen, und Kennung und
/// Pfad lassen sich in beide Richtungen auflösen. Ein unbekanntes Ziel ergibt
/// <see langword="null"/> statt einer Ausnahme.
/// </summary>
public sealed class NavigationAndLocatorTests
{
    [Fact]
    public async Task FindByWikiLinkAsync_OnAKnownTarget_AsksTheWikiLinkResolver()
    {
        // Die Vorschau schreibt den Slug ins Ziel (projekt-x), die Datei heißt "Projekt X".
        // Ein Vergleich mit dem Dateinamen fände sie nicht; der Resolver vergleicht Slugs.
        FakeWikiLinkResolver resolver = new();
        Guid expected = new("11111111-1111-1111-1111-111111111111");
        resolver.Map("projekt-x", expected);
        using ServiceProvider provider = BuildProvider(new FakeMarkdownFileRepository(), resolver);
        MarkdownFileDocumentLocator locator = new(provider.GetRequiredService<IServiceScopeFactory>());

        Guid? result = await locator.FindByWikiLinkAsync("projekt-x", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task FindByWikiLinkAsync_OnAnUnknownTarget_ReturnsNull()
    {
        using ServiceProvider provider = BuildProvider(new FakeMarkdownFileRepository(), new FakeWikiLinkResolver());
        MarkdownFileDocumentLocator locator = new(provider.GetRequiredService<IServiceScopeFactory>());

        Guid? result = await locator.FindByWikiLinkAsync("unbekannt", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAbsolutePathAsync_OnAnIndexedId_ReturnsItsPath()
    {
        // Rückweg der Auflösung: Das Graph-Fenster kennt nur die Kennung und braucht
        // den Pfad, um die Datei zu öffnen.
        FakeMarkdownFileRepository repo = new();
        Guid id = new("33333333-3333-3333-3333-333333333333");
        repo.Add(new MarkdownFile
        {
            Id = id,
            AbsolutePath = @"C:\notizen\Ziel.md",
            RelativePath = "Ziel.md",
            FileNameWithoutExtension = "Ziel",
            ContentHash = "h",
        });

        using ServiceProvider provider = BuildProvider(repo);
        MarkdownFileDocumentLocator locator = new(provider.GetRequiredService<IServiceScopeFactory>());

        string? result = await locator.GetAbsolutePathAsync(id, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(@"C:\notizen\Ziel.md", result, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetAbsolutePathAsync_OnAnUnknownId_ReturnsNull()
    {
        FakeMarkdownFileRepository repo = new();
        using ServiceProvider provider = BuildProvider(repo);
        MarkdownFileDocumentLocator locator = new(provider.GetRequiredService<IServiceScopeFactory>());

        string? result = await locator.GetAbsolutePathAsync(new Guid("44444444-4444-4444-4444-444444444444"), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAbsolutePathAsync_WithoutAnId_ReturnsNullWithoutTouchingTheDatabase()
    {
        // Guid.Empty bedeutet "nichts ausgewählt"; dafür lohnt kein Datenbank-Zugriff. Gezählt
        // wird, wie oft ein Scope das Repository anfordert: Jede Anforderung öffnet einen Kontext.
        int repositoryRequests = 0;
        ServiceCollection services = new();
        _ = services.AddScoped<IMarkdownFileRepository>(_ =>
        {
            repositoryRequests++;
            return new FakeMarkdownFileRepository();
        });
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        MarkdownFileDocumentLocator locator = new(provider.GetRequiredService<IServiceScopeFactory>());

        string? result = await locator.GetAbsolutePathAsync(Guid.Empty, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Null(result);
        Assert.Equal(0, repositoryRequests);
    }

    [Fact]
    public async Task FindByAbsolutePathAsync_OnAnIndexedFile_ReturnsItsId()
    {
        FakeMarkdownFileRepository repo = new();
        Guid expected = new("55555555-5555-5555-5555-555555555555");
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

        Guid? result = await locator.FindByAbsolutePathAsync(@"C:\notes\Pfad.md", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task FindByAbsolutePathAsync_OnAnUnknownPath_ReturnsNull()
    {
        FakeMarkdownFileRepository repo = new();
        using ServiceProvider provider = BuildProvider(repo);
        MarkdownFileDocumentLocator locator = new(provider.GetRequiredService<IServiceScopeFactory>());

        Guid? result = await locator.FindByAbsolutePathAsync(@"C:\notes\nicht-indiziert.md", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindByWikiLinkAsync_CalledTwice_ResolvesAgainEachTime()
    {
        // Merkte sich der Locator ein Ergebnis, fände er eine erst später indizierte Datei
        // nicht.
        FakeWikiLinkResolver resolver = new();
        using ServiceProvider provider = BuildProvider(new FakeMarkdownFileRepository(), resolver);
        MarkdownFileDocumentLocator locator = new(provider.GetRequiredService<IServiceScopeFactory>());

        Guid? first = await locator.FindByWikiLinkAsync("nachgetragen", TestContext.Current.CancellationToken).ConfigureAwait(true);
        Guid expected = new("22222222-2222-2222-2222-222222222222");
        resolver.Map("nachgetragen", expected);
        Guid? second = await locator.FindByWikiLinkAsync("nachgetragen", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Null(first);
        Assert.Equal(expected, second);
    }

    private static ServiceProvider BuildProvider(FakeMarkdownFileRepository repository) =>
        BuildProvider(repository, new FakeWikiLinkResolver());

    private static ServiceProvider BuildProvider(FakeMarkdownFileRepository repository, FakeWikiLinkResolver resolver)
    {
        ServiceCollection services = new();
        _ = services.AddScoped<IMarkdownFileRepository>(_ => repository);
        _ = services.AddScoped<IWikiLinkResolver>(_ => resolver);
        return services.BuildServiceProvider(validateScopes: true);
    }
}
