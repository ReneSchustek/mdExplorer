using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>Tests fuer das ViewModel des "Alle Dateien"-Tabs.</summary>
public sealed class AllFilesViewModelTests
{
    private static readonly Guid FileAId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FileBId = new("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime OlderUtc = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NewerUtc = new(2026, 6, 9, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RefreshAsync_LoadsItemsFromQuery_OrderedByLastModifiedDesc()
    {
        FakeAllFilesQuery query = new([
            new AllFilesRow(FileAId, "Alpha", "Sub/Alpha.md", @"C:\notes\Sub\Alpha.md", OlderUtc, ["projekt"]),
            new AllFilesRow(FileBId, "Beta", "Beta.md", @"C:\notes\Beta.md", NewerUtc, ["projekt", "wichtig"]),
        ]);
        using ServiceProvider provider = BuildProvider(query);
        AllFilesViewModel sut = new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AllFilesViewModel>.Instance);

        await sut.RefreshAsync().ConfigureAwait(true);

        Assert.Equal(2, sut.Items.Count);
        Assert.Equal("Beta", sut.Items[0].Title);
        Assert.Equal("Alpha", sut.Items[1].Title);
    }

    [Fact]
    public async Task SearchText_FiltersOnTitlePathAndTagSlugs()
    {
        FakeAllFilesQuery query = new([
            new AllFilesRow(FileAId, "Alpha", "Sub/Alpha.md", @"C:\notes\Sub\Alpha.md", OlderUtc, ["projekt"]),
            new AllFilesRow(FileBId, "Beta", "Beta.md", @"C:\notes\Beta.md", NewerUtc, ["wichtig"]),
        ]);
        using ServiceProvider provider = BuildProvider(query);
        AllFilesViewModel sut = new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AllFilesViewModel>.Instance);
        await sut.RefreshAsync().ConfigureAwait(true);

        sut.SearchText = "alpha";
        AllFilesItemViewModel onlyAlphaByTitle = Assert.Single(sut.Items);
        Assert.Equal(FileAId, onlyAlphaByTitle.MarkdownFileId);

        sut.SearchText = "sub/";
        AllFilesItemViewModel onlyByPath = Assert.Single(sut.Items);
        Assert.Equal(FileAId, onlyByPath.MarkdownFileId);

        sut.SearchText = "wichtig";
        AllFilesItemViewModel onlyByTag = Assert.Single(sut.Items);
        Assert.Equal(FileBId, onlyByTag.MarkdownFileId);

        sut.SearchText = string.Empty;
        Assert.Equal(2, sut.Items.Count);
    }

    [Fact]
    public async Task SortMode_TitleAndRelativePath_AreApplied()
    {
        FakeAllFilesQuery query = new([
            new AllFilesRow(FileAId, "Beta", "z/beta.md", @"C:\notes\z\beta.md", OlderUtc, []),
            new AllFilesRow(FileBId, "Alpha", "a/alpha.md", @"C:\notes\a\alpha.md", NewerUtc, []),
        ]);
        using ServiceProvider provider = BuildProvider(query);
        AllFilesViewModel sut = new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AllFilesViewModel>.Instance);
        await sut.RefreshAsync().ConfigureAwait(true);

        sut.SortMode = AllFilesSortMode.Title;
        Assert.Equal("Alpha", sut.Items[0].Title);

        sut.SortMode = AllFilesSortMode.RelativePath;
        Assert.Equal("a/alpha.md", sut.Items[0].RelativePath);
    }

    [Fact]
    public async Task SelectedItem_FiresFileSelectedEventWithAbsolutePath()
    {
        FakeAllFilesQuery query = new([
            new AllFilesRow(FileAId, "Alpha", "Alpha.md", @"C:\notes\Alpha.md", OlderUtc, []),
        ]);
        using ServiceProvider provider = BuildProvider(query);
        AllFilesViewModel sut = new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AllFilesViewModel>.Instance);
        await sut.RefreshAsync().ConfigureAwait(true);

        string? raised = null;
        sut.FileSelected += path => raised = path;
        sut.SelectedItem = sut.Items[0];

        Assert.Equal(@"C:\notes\Alpha.md", raised);
    }

    private static ServiceProvider BuildProvider(IAllFilesQuery query)
    {
        ServiceCollection services = new();
        _ = services.AddScoped(_ => query);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class FakeAllFilesQuery(IReadOnlyList<AllFilesRow> rows) : IAllFilesQuery
    {
        public Task<IReadOnlyList<AllFilesRow>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult(rows);
    }
}
