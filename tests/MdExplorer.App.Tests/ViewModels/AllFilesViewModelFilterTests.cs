using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Tests für die Filter der Datei-Liste — Ordner, Kennzeichnung und Änderungszeitraum.
/// </summary>
/// <remarks>
/// Der Zeitraum hängt an der Uhr, deshalb läuft er gegen eine gestellte Zeit. Ein Test, der
/// die Wanduhr befragt, wäre am 1. Januar ein anderer als am 15. August.
/// </remarks>
public sealed class AllFilesViewModelFilterTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid RootFileId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SubFileId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OldFileId = new("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task FilterByFolder_KeepsOnlyThatFolder_AndShowsAsRemovableChip()
    {
        AllFilesViewModel sut = await LoadAsync().ConfigureAwait(true);

        sut.FilterByFolderCommand.Execute("Sub");

        AllFilesItemViewModel remaining = Assert.Single(sut.Items);
        Assert.Equal(SubFileId, remaining.MarkdownFileId);
        ActiveFilterViewModel chip = Assert.Single(sut.ActiveFilters);
        Assert.Equal(AllFilesFilterKind.Folder, chip.Kind);
        Assert.Equal("Ordner: Sub", chip.Label);

        sut.RemoveFilterCommand.Execute(chip);

        Assert.Equal(3, sut.Items.Count);
        Assert.Empty(sut.ActiveFilters);
    }

    [Fact]
    public async Task FilterByFolder_OnRootLevel_MatchesFilesWithoutFolder()
    {
        AllFilesViewModel sut = await LoadAsync().ConfigureAwait(true);

        sut.FilterByFolderCommand.Execute(string.Empty);

        AllFilesItemViewModel remaining = Assert.Single(sut.Items);
        Assert.Equal(RootFileId, remaining.MarkdownFileId);
        ActiveFilterViewModel chip = Assert.Single(sut.ActiveFilters);
        Assert.Equal("Ordner: (Wurzel)", chip.Label);
    }

    [Fact]
    public async Task FilterByTag_AppliedTwice_KeepsOnlyEntriesCarryingBothTags()
    {
        AllFilesViewModel sut = await LoadAsync().ConfigureAwait(true);

        sut.FilterByTagCommand.Execute("projekt");
        Assert.Equal(2, sut.Items.Count);

        sut.FilterByTagCommand.Execute("wichtig");

        AllFilesItemViewModel remaining = Assert.Single(sut.Items);
        Assert.Equal(RootFileId, remaining.MarkdownFileId);
        Assert.Equal(2, sut.ActiveFilters.Count);
    }

    [Fact]
    public async Task FilterByTag_AppliedTwiceWithSameValue_AddsOneChipOnly()
    {
        AllFilesViewModel sut = await LoadAsync().ConfigureAwait(true);

        sut.FilterByTagCommand.Execute("projekt");
        sut.FilterByTagCommand.Execute("projekt");

        _ = Assert.Single(sut.ActiveFilters);
    }

    [Fact]
    public async Task SelectPeriod_Today_KeepsOnlyEntriesChangedSinceMidnight()
    {
        AllFilesViewModel sut = await LoadAsync().ConfigureAwait(true);

        sut.SelectPeriodCommand.Execute(AllFilesPeriod.Today);

        Assert.DoesNotContain(sut.Items, item => item.MarkdownFileId == OldFileId);
        ActiveFilterViewModel chip = Assert.Single(sut.ActiveFilters);
        Assert.Equal("Geändert: Heute", chip.Label);
        Assert.True(sut.PeriodFilters.Single(filter => filter.Period == AllFilesPeriod.Today).IsActive);
        Assert.False(sut.PeriodFilters.Single(filter => filter.Period == AllFilesPeriod.Any).IsActive);
    }

    [Fact]
    public async Task SelectPeriod_ThirtyDays_KeepsTheEntryFromTwoWeeksAgo()
    {
        AllFilesViewModel sut = await LoadAsync().ConfigureAwait(true);

        sut.SelectPeriodCommand.Execute(AllFilesPeriod.LastThirtyDays);

        Assert.Contains(sut.Items, item => item.MarkdownFileId == OldFileId);
    }

    [Fact]
    public async Task SelectPeriod_BackToAny_DropsTheChip()
    {
        AllFilesViewModel sut = await LoadAsync().ConfigureAwait(true);
        sut.SelectPeriodCommand.Execute(AllFilesPeriod.Today);

        sut.SelectPeriodCommand.Execute(AllFilesPeriod.Any);

        Assert.Empty(sut.ActiveFilters);
        Assert.Equal(3, sut.Items.Count);
    }

    [Fact]
    public async Task ResetSearchAndFilters_ClearsEverything_EvenWithoutSearchText()
    {
        AllFilesViewModel sut = await LoadAsync().ConfigureAwait(true);
        sut.FilterByFolderCommand.Execute("Sub");
        sut.FilterByTagCommand.Execute("projekt");
        sut.SelectPeriodCommand.Execute(AllFilesPeriod.Today);

        sut.ResetSearchAndFiltersCommand.Execute(null);

        Assert.Empty(sut.ActiveFilters);
        Assert.Equal(3, sut.Items.Count);
    }

    [Fact]
    public async Task ResetSearchAndFilters_AlsoClearsTheSearchText()
    {
        AllFilesViewModel sut = await LoadAsync().ConfigureAwait(true);
        sut.SearchText = "alpha";
        sut.FilterByTagCommand.Execute("projekt");

        sut.ResetSearchAndFiltersCommand.Execute(null);

        Assert.Empty(sut.SearchText);
        Assert.Equal(3, sut.Items.Count);
    }

    [Fact]
    public async Task Filters_WithoutAnyMatch_ReportNoMatchesInsteadOfEmptyStock()
    {
        AllFilesViewModel sut = await LoadAsync().ConfigureAwait(true);

        sut.FilterByTagCommand.Execute("gibt-es-nicht");

        Assert.Empty(sut.Items);
        Assert.True(sut.ShowsNoMatches);
        Assert.False(sut.ShowsNothingAtAll);
    }

    [Fact]
    public async Task FolderPath_IsTakenFromTheRelativePath_ForBothSeparators()
    {
        AllFilesViewModel sut = await LoadAsync().ConfigureAwait(true);
        sut.SortMode = AllFilesSortMode.RelativePath;

        Assert.Equal(string.Empty, sut.Items.Single(item => item.MarkdownFileId == RootFileId).FolderPath);
        Assert.Equal("Sub", sut.Items.Single(item => item.MarkdownFileId == SubFileId).FolderPath);
        Assert.Equal("Alt/Tief", sut.Items.Single(item => item.MarkdownFileId == OldFileId).FolderPath);
    }

    /// <remarks>
    /// Der Zustandserhalt der Linie: Wer aus einem Dokument zurückkommt, findet seine
    /// Einschränkung wieder vor. Ein erneutes Laden ist der härtere Fall — dabei wird die
    /// Liste vollständig neu aufgebaut.
    /// </remarks>
    [Fact]
    public async Task SearchAndFilters_SurviveAReload()
    {
        AllFilesViewModel sut = await LoadAsync().ConfigureAwait(true);
        sut.SearchText = "a";
        sut.FilterByTagCommand.Execute("projekt");
        sut.SelectPeriodCommand.Execute(AllFilesPeriod.LastThirtyDays);

        await sut.RefreshAsync().ConfigureAwait(true);

        Assert.Equal("a", sut.SearchText);
        Assert.Equal(2, sut.ActiveFilters.Count);
        Assert.All(sut.Items, item => Assert.Contains("projekt", item.TagSlugs));
    }

    [Fact]
    public async Task WhenLoadingFails_SaysSo_InsteadOfClaimingAnEmptyStock()
    {
        ServiceCollection services = new();
        _ = services.AddScoped<IAllFilesQuery>(_ => new FailingAllFilesQuery());
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        AllFilesViewModel sut = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeTimeProvider(NowUtc),
            NullLogger<AllFilesViewModel>.Instance);

        await sut.RefreshAsync().ConfigureAwait(true);

        Assert.True(sut.ShowsLoadFailure);
        Assert.False(sut.ShowsNothingAtAll);
        Assert.False(sut.ShowsNoMatches);
        Assert.False(sut.IsBusy);
    }

    private static async Task<AllFilesViewModel> LoadAsync()
    {
        // Ein Eintrag von heute in der Wurzel, einer von heute im Unterordner, einer von
        // vor zwei Wochen in einem Pfad mit Rückwärtsschrägstrich.
        FakeAllFilesQuery query = new(
        [
            new AllFilesRow(RootFileId, "Alpha", "Alpha.md", @"C:\notes\Alpha.md", NowUtc.AddHours(-1), ["projekt", "wichtig"]),
            new AllFilesRow(SubFileId, "Beta", "Sub/Beta.md", @"C:\notes\Sub\Beta.md", NowUtc.AddHours(-2), ["projekt"]),
            new AllFilesRow(OldFileId, "Gamma", @"Alt\Tief\Gamma.md", @"C:\notes\Alt\Tief\Gamma.md", NowUtc.AddDays(-14), []),
        ]);

        ServiceCollection services = new();
        _ = services.AddScoped<IAllFilesQuery>(_ => query);
        ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        FakeTimeProvider clock = new(NowUtc);
        AllFilesViewModel viewModel = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<AllFilesViewModel>.Instance);

        await viewModel.RefreshAsync().ConfigureAwait(true);

        return viewModel;
    }

    private sealed class FakeAllFilesQuery(IReadOnlyList<AllFilesRow> rows) : IAllFilesQuery
    {
        public Task<IReadOnlyList<AllFilesRow>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult(rows);
    }

    /// <summary>Ein Bestand, der sich nicht abrufen lässt — die Lage, die wie „leer" aussieht.</summary>
    private sealed class FailingAllFilesQuery : IAllFilesQuery
    {
        public Task<IReadOnlyList<AllFilesRow>> GetAllAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Der Index ist nicht ansprechbar.");
    }
}
