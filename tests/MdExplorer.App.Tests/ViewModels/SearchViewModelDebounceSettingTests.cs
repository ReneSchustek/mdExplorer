using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Messaging;
using MdExplorer.Core.Models;
using MdExplorer.Search.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Die Wartezeit nach dem letzten Tastenanschlag ist eine Einstellung im Dialog „Verhalten".
/// Wird sie nicht gelesen, ändert der Nutzer einen Wert, der nichts bewirkt.
/// </summary>
public sealed class SearchViewModelDebounceSettingTests
{
    private const int StartupDebounceMs = 750;
    private const int ChangedDebounceMs = 1_200;

    [Fact]
    public void Construction_TakesTheWaitTimeFromTheSettings()
    {
        RecordingTimeProvider clock = new();
        using ServiceProvider provider = BuildProvider();
        StrongReferenceMessenger messenger = new();
        using SearchViewModel sut = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            messenger,
            new FakeSettingsService(WithDebounce(StartupDebounceMs)),
            new ImmediateUiDispatcher(),
            NullLogger<SearchViewModel>.Instance);

        sut.QueryText = "bericht";

        Assert.Equal(TimeSpan.FromMilliseconds(StartupDebounceMs), clock.LastDueTime);
    }

    [Fact]
    public void ASettingsChange_TakesEffectForTheNextInput()
    {
        RecordingTimeProvider clock = new();
        using ServiceProvider provider = BuildProvider();
        StrongReferenceMessenger messenger = new();
        using SearchViewModel sut = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            messenger,
            new FakeSettingsService(WithDebounce(StartupDebounceMs)),
            new ImmediateUiDispatcher(),
            NullLogger<SearchViewModel>.Instance);

        _ = messenger.Send(new SettingsChangedMessage(WithDebounce(StartupDebounceMs), WithDebounce(ChangedDebounceMs)));
        sut.QueryText = "bericht";

        Assert.Equal(TimeSpan.FromMilliseconds(ChangedDebounceMs), clock.LastDueTime);
    }

    private static AppSettings WithDebounce(int milliseconds) =>
        AppSettings.Default with { Behavior = AppSettings.Default.Behavior with { SearchDebounceMs = milliseconds } };

    private static ServiceProvider BuildProvider()
    {
        FakeSearchService searchService = new();
        searchService.SetNextResults([]);
        ServiceCollection services = new();
        _ = services.AddScoped<ISearchService>(_ => searchService);
        return services.BuildServiceProvider(validateScopes: true);
    }

    // Hält die Wartezeit fest, mit der die Suche ihren Zeitgeber anlegt. Der Zeitgeber selbst
    // läuft nie ab, damit der Test nicht von der Wanduhr abhängt.
    private sealed class RecordingTimeProvider : TimeProvider
    {
        public TimeSpan? LastDueTime { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            LastDueTime = dueTime;
            return System.CreateTimer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }
}
