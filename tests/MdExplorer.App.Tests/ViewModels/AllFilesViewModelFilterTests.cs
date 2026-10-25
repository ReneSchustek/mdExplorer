using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Die Filter der Datei-Liste nach Ordner, Schlagwort und Änderungszeitraum greifen
/// zusammen, erscheinen als entfernbare Chips und überstehen ein Neuladen.
/// </summary>
/// <remarks>
/// Der Zeitraum hängt an der Uhr, deshalb läuft er gegen eine gestellte Zeit. Ein Test, der
/// die Wanduhr befragt, wäre am 1. Januar ein anderer als am 15. August.
/// </remarks>
public sealed class AllFilesViewModelFilterTests : IDisposable
{
    private static readonly DateTime NowUtc = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid RootFileId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SubFileId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OldFileId = new("33333333-3333-3333-3333-333333333333");

    private readonly List<ServiceProvider> _providers = [];

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (ServiceProvider provider in _providers)
        {
            provider.Dispose();
        }
    }

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
    public async Task SelectPeriod_Today_CountsFromLocalMidnight_NotFromMidnightInUtc()
    {
        // „Heute" ist der Tag auf der Uhr neben dem Bildschirm. Zwei Stunden vor UTC beginnt
        // er um 22:00 UTC des Vortags; eine Datei von ein Uhr nachts gehört schon dazu.
        Guid earlyTodayId = new("44444444-4444-4444-4444-444444444444");
        Guid lateYesterdayId = new("55555555-5555-5555-5555-555555555555");
        TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone("MdExplorer-PlusTwo", TimeSpan.FromHours(2), "PlusTwo", "PlusTwo");
        DateTime nowUtc = new(2026, 8, 15, 21, 30, 0, DateTimeKind.Utc);
        FakeAllFilesQuery query = new(
        [
            new AllFilesRow(earlyTodayId, "Early", "Early.md", @"C:\notes\Early.md", new DateTime(2026, 8, 14, 23, 0, 0, DateTimeKind.Utc), []),
            new AllFilesRow(lateYesterdayId, "Late", "Late.md", @"C:\notes\Late.md", new DateTime(2026, 8, 14, 21, 0, 0, DateTimeKind.Utc), []),
        ]);
        ServiceCollection services = new();
        _ = services.AddScoped<IAllFilesQuery>(_ => query);
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        FakeTimeProvider clock = new(nowUtc);
        clock.SetLocalTimeZone(zone);
        AllFilesViewModel sut = new(provider.GetRequiredService<IServiceScopeFactory>(), clock, NullLogger<AllFilesViewModel>.Instance);
        await sut.RefreshAsync().ConfigureAwait(true);

        sut.SelectPeriodCommand.Execute(AllFilesPeriod.Today);

        AllFilesItemViewModel remaining = Assert.Single(sut.Items);
        Assert.Equal(earlyTodayId, remaining.MarkdownFileId);
    }

    [Fact]
    public async Task SelectPeriod_Today_OnADayWithoutMidnight_StartsAtTheFirstValidHour()
    {
        // Manche Zonen stellen die Uhr um Mitternacht vor. 00:00 gibt es an diesem Tag nicht;
        // der Filter darf daran nicht scheitern, und der Tag beginnt um 01:00 Ortszeit.
        Guid todayId = new("66666666-6666-6666-6666-666666666666");
        Guid yesterdayId = new("77777777-7777-7777-7777-777777777777");
        TimeZoneInfo.AdjustmentRule springForwardAtMidnight = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 8, 15),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 11, 1));
        TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone(
            "MdExplorer-MidnightShift", TimeSpan.FromHours(2), "MidnightShift", "MidnightShift", "MidnightShiftSummer", [springForwardAtMidnight]);
        FakeAllFilesQuery query = new(
        [
            new AllFilesRow(todayId, "Heute", "Heute.md", @"C:\notes\Heute.md", new DateTime(2026, 8, 14, 22, 30, 0, DateTimeKind.Utc), []),
            new AllFilesRow(yesterdayId, "Gestern", "Gestern.md", @"C:\notes\Gestern.md", new DateTime(2026, 8, 14, 21, 30, 0, DateTimeKind.Utc), []),
        ]);
        ServiceCollection services = new();
        _ = services.AddScoped<IAllFilesQuery>(_ => query);
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        FakeTimeProvider clock = new(new DateTime(2026, 8, 15, 9, 0, 0, DateTimeKind.Utc));
        clock.SetLocalTimeZone(zone);
        AllFilesViewModel sut = new(provider.GetRequiredService<IServiceScopeFactory>(), clock, NullLogger<AllFilesViewModel>.Instance);
        await sut.RefreshAsync().ConfigureAwait(true);

        sut.SelectPeriodCommand.Execute(AllFilesPeriod.Today);

        AllFilesItemViewModel remaining = Assert.Single(sut.Items);
        Assert.Equal(todayId, remaining.MarkdownFileId);
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
    /// Wer aus einem Dokument zurückkommt, findet seine Einschränkung wieder vor. Ein erneutes Laden ist der härtere Fall; dabei wird die
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

    [Theory]
    [InlineData(TestFailures.Database)]
    [InlineData(TestFailures.InvalidState)]
    public async Task WhenLoadingFails_SaysSo_InsteadOfClaimingAnEmptyStock(string kind)
    {
        ServiceCollection services = new();
        _ = services.AddScoped<IAllFilesQuery>(_ => new FailingAllFilesQuery(TestFailures.Of(kind)));
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

    [Fact]
    public async Task Refresh_RequestedWhileLoading_RunsAgainAfterwards()
    {
        // Nach Umbenennen oder Löschen wird die Liste nachgeladen. Lief gerade ein Laden, kehrte
        // der zweite Auftrag still zurück, und die Liste zeigte den Pfad, den es nicht mehr gibt.
        GatedAllFilesQuery query = new([new AllFilesRow(RootFileId, "Alpha", "Alpha.md", @"C:\notes\Alpha.md", NowUtc, [])]);
        ServiceCollection services = new();
        _ = services.AddScoped<IAllFilesQuery>(_ => query);
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        AllFilesViewModel sut = new(provider.GetRequiredService<IServiceScopeFactory>(), new FakeTimeProvider(NowUtc), NullLogger<AllFilesViewModel>.Instance);

        Task first = sut.RefreshAsync();
        Task second = sut.RefreshAsync();
        TaskCompletionSource firstGate = query.Gate;
        query.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        query.Gate.SetResult();
        firstGate.SetResult();
        await Task.WhenAll(first, second).ConfigureAwait(true);

        Assert.Equal(2, query.CallCount);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task PeriodFilter_AsksTheClockOncePerRebuild_NotOncePerEntry()
    {
        AllFilesRow[] rows =
        [
            .. Enumerable.Range(0, 20).Select(index => new AllFilesRow(
                new Guid(index + 1, 0, 0, new byte[8]),
                $"Notiz{index:D2}",
                $"Notiz{index:D2}.md",
                $@"C:\notes\Notiz{index:D2}.md",
                NowUtc.AddHours(-index),
                [])),
        ];
        ServiceCollection services = new();
        _ = services.AddScoped<IAllFilesQuery>(_ => new FakeAllFilesQuery(rows));
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        CountingTimeProvider clock = new(NowUtc);
        AllFilesViewModel sut = new(provider.GetRequiredService<IServiceScopeFactory>(), clock, NullLogger<AllFilesViewModel>.Instance);
        await sut.RefreshAsync().ConfigureAwait(true);
        int before = clock.NowQueries;

        sut.SelectPeriodCommand.Execute(AllFilesPeriod.Today);

        Assert.Equal(1, clock.NowQueries - before);
    }

    /// <remarks>
    /// Die Befehle kommen aus dem Markup und können mit <see langword="null"/> ausgelöst
    /// werden; bei einer leeren Liste tut WPF genau das. Keiner davon darf dabei einen
    /// Filter setzen, den der Nutzer nicht angefordert hat.
    /// </remarks>
    [Fact]
    public async Task Commands_CalledWithoutAValue_SetNoFilter()
    {
        AllFilesViewModel sut = await LoadAsync().ConfigureAwait(true);

        sut.FilterByFolderCommand.Execute(null);
        sut.FilterByTagCommand.Execute(null);
        sut.FilterByTagCommand.Execute("   ");
        sut.RemoveFilterCommand.Execute(null);

        Assert.Empty(sut.ActiveFilters);
        Assert.Equal(3, sut.Items.Count);
    }

    /// <remarks>
    /// Denselben Zeitraum noch einmal zu wählen ist kein Vorgang. Ohne die Prüfung liefe die
    /// Liste bei jedem Klick neu durch, sichtbar als Sprung in der Bildlaufleiste.
    /// </remarks>
    [Fact]
    public async Task SelectPeriod_WithTheSameValueAgain_ChangesNothing()
    {
        AllFilesViewModel sut = await LoadAsync().ConfigureAwait(true);
        sut.SelectPeriodCommand.Execute(AllFilesPeriod.Today);
        int afterFirstTime = sut.Items.Count;

        sut.SelectPeriodCommand.Execute(AllFilesPeriod.Today);

        Assert.Equal(afterFirstTime, sut.Items.Count);
        _ = Assert.Single(sut.ActiveFilters);
    }

    /// <remarks>
    /// Der Sprung ist ein Ereignis, kein Filter: Die Leiste rollt zur Gruppe, der Bestand
    /// bleibt vollständig. Ein Buchstabe ohne Wert darf gar nichts auslösen.
    /// </remarks>
    [Fact]
    public async Task JumpToLetter_RaisesTheLetterAndKeepsTheStock()
    {
        AllFilesViewModel sut = await LoadAsync().ConfigureAwait(true);
        List<char> jumped = [];
        sut.JumpRequested += jumped.Add;

        sut.JumpToLetterCommand.Execute("M");
        sut.JumpToLetterCommand.Execute(null);
        sut.JumpToLetterCommand.Execute(string.Empty);

        Assert.Equal('M', Assert.Single(jumped));
        Assert.Equal(3, sut.Items.Count);
    }

    /// <remarks>
    /// Die Auswahl meldet den Pfad weiter. Daran hängt, welches Dokument die Vorschau zeigt.
    /// Wird die Auswahl wieder geleert, darf nichts gemeldet werden. Sonst öffnete ein
    /// Neuladen der Liste ungefragt das zuletzt markierte Dokument.
    /// </remarks>
    [Fact]
    public async Task SelectedItem_ReportsThePath_ButNotWhenItIsClearedAgain()
    {
        AllFilesViewModel sut = await LoadAsync().ConfigureAwait(true);
        List<string> reported = [];
        sut.FileSelected += reported.Add;

        sut.SelectedItem = sut.Items[0];
        sut.SelectedItem = null;

        string single = Assert.Single(reported);
        Assert.Equal(sut.Items[0].AbsolutePath, single, StringComparer.Ordinal);
    }

    /// <remarks>
    /// Die Sprungleiste erscheint erst ab 50 Einträgen und nur bei Sortierung nach Titel oder
    /// Pfad. Geprüft wird die Schwelle von beiden Seiten in einer Sortierung, in der die Leiste
    /// überhaupt erscheinen darf; sonst bliebe der Test auch bei kaputter Schwelle grün.
    /// </remarks>
    [Theory]
    [InlineData((int)AllFilesSortMode.Title, 49, false)]
    [InlineData((int)AllFilesSortMode.Title, 50, true)]
    [InlineData((int)AllFilesSortMode.RelativePath, 50, true)]
    [InlineData((int)AllFilesSortMode.LastModified, 50, false)]
    public async Task JumpBar_AppearsFromFiftyEntriesInAnAlphabeticalOrder(int sortMode, int entryCount, bool expected)
    {
        AllFilesViewModel sut = await LoadManyAsync(entryCount).ConfigureAwait(true);

        sut.SortMode = (AllFilesSortMode)sortMode;

        Assert.Equal(expected, sut.IsJumpBarVisible);
    }

    private async Task<AllFilesViewModel> LoadManyAsync(int count)
    {
        AllFilesRow[] rows =
        [
            .. Enumerable.Range(0, count).Select(index => new AllFilesRow(
                new Guid(index + 1, 0, 0, new byte[8]),
                $"Notiz {index:D3}",
                $"Notiz{index:D3}.md",
                $@"C:\notes\Notiz{index:D3}.md",
                NowUtc.AddHours(-index),
                [])),
        ];
        ServiceCollection services = new();
        _ = services.AddScoped<IAllFilesQuery>(_ => new FakeAllFilesQuery(rows));
        ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        _providers.Add(provider);
        AllFilesViewModel viewModel = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeTimeProvider(NowUtc),
            NullLogger<AllFilesViewModel>.Instance);

        await viewModel.RefreshAsync().ConfigureAwait(true);

        return viewModel;
    }

    private async Task<AllFilesViewModel> LoadAsync()
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
        _providers.Add(provider);

        FakeTimeProvider clock = new(NowUtc);
        AllFilesViewModel viewModel = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<AllFilesViewModel>.Instance);

        await viewModel.RefreshAsync().ConfigureAwait(true);

        return viewModel;
    }

    /// <summary>Liefert die vorgegebenen Zeilen unverändert.</summary>
    private sealed class FakeAllFilesQuery(IReadOnlyList<AllFilesRow> rows) : IAllFilesQuery
    {
        public Task<IReadOnlyList<AllFilesRow>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult(rows);
    }

    /// <summary>
    /// Ein Bestand, der sich nicht abrufen lässt. Ohne eigene Meldung sähe das aus wie ein
    /// leerer Bestand.
    /// </summary>
    private sealed class FailingAllFilesQuery(Exception failure) : IAllFilesQuery
    {
        public Task<IReadOnlyList<AllFilesRow>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<AllFilesRow>>(failure);
    }

    /// <summary>Hält jede Abfrage an, bis der Test sie freigibt, und zählt sie.</summary>
    private sealed class GatedAllFilesQuery(IReadOnlyList<AllFilesRow> rows) : IAllFilesQuery
    {
        public TaskCompletionSource Gate { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public async Task<IReadOnlyList<AllFilesRow>> GetAllAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            await Gate.Task.ConfigureAwait(false);
            return rows;
        }
    }

    /// <summary>Eine gestellte Uhr, die zählt, wie oft nach der Zeit gefragt wird.</summary>
    private sealed class CountingTimeProvider(DateTime nowUtc) : TimeProvider
    {
        public int NowQueries { get; private set; }

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow()
        {
            NowQueries++;
            return new DateTimeOffset(nowUtc);
        }
    }
}
