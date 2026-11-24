using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Nur die zuletzt geplante Anfrage darf laufen, auch wenn der Zeitgeber einer früheren schon
/// ausgelöst hat, während die neue geplant wird.
/// </summary>
/// <remarks>
/// <c>ITimer.Dispose</c> hält einen bereits laufenden Rückruf nicht auf. Der wartet dann auf die
/// Sperre und machte sich danach ungeprüft zum aktuellen Lauf: Nach „Abbrechen“ erschienen alte
/// Treffer, nach neuer Eingabe kurz die der vorigen. Der Zeitanbieter hier hält die Rückrufe fest,
/// damit genau dieser verspätete Aufruf nachgestellt werden kann.
/// </remarks>
public sealed class DebouncedSearchRunnerTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(300);

    [Fact]
    public void ALateCallbackOfAReplacedTimer_StartsNoSearch()
    {
        FakeSearchService search = new();
        CapturingTimeProvider time = new();
        using ServiceProvider provider = Provider(search);
        using DebouncedSearchRunner sut = Runner(provider, time);

        sut.Schedule(new SearchQuery("alt"), Delay);
        sut.Schedule(new SearchQuery("neu"), Delay);
        time.Fire(0);

        Assert.Equal(0, search.CallCount);
    }

    [Fact]
    public void ALateCallbackAfterCancel_StartsNoSearch()
    {
        FakeSearchService search = new();
        CapturingTimeProvider time = new();
        using ServiceProvider provider = Provider(search);
        using DebouncedSearchRunner sut = Runner(provider, time);

        sut.Schedule(new SearchQuery("alt"), Delay);
        sut.Cancel();
        time.Fire(0);

        Assert.Equal(0, search.CallCount);
    }

    [Fact]
    public void TheCallbackOfTheCurrentTimer_StartsTheSearch()
    {
        FakeSearchService search = new();
        CapturingTimeProvider time = new();
        using ServiceProvider provider = Provider(search);
        using DebouncedSearchRunner sut = Runner(provider, time);

        sut.Schedule(new SearchQuery("alt"), Delay);
        sut.Schedule(new SearchQuery("neu"), Delay);
        time.Fire(1);

        Assert.Equal(1, search.CallCount);
        Assert.Equal("neu", Assert.Single(search.ReceivedQueries).Text);
    }

    private static ServiceProvider Provider(FakeSearchService search)
    {
        ServiceCollection services = new();
        _ = services.AddScoped<ISearchService>(_ => search);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static DebouncedSearchRunner Runner(ServiceProvider provider, TimeProvider time) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            time,
            new ImmediateUiDispatcher(),
            NullLogger.Instance,
            _ => { },
            () => { });

    /// <summary>Hält jeden angelegten Zeitgeber fest; ausgelöst wird nur von Hand.</summary>
    private sealed class CapturingTimeProvider : TimeProvider
    {
        private readonly List<(TimerCallback Callback, object? State)> _timers = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _timers.Add((callback, state));
            return new InertTimer();
        }

        /// <summary>Ruft den Rückruf des <paramref name="index"/>-ten Zeitgebers auf, auch wenn er schon entsorgt ist.</summary>
        public void Fire(int index) => _timers[index].Callback(_timers[index].State);
    }

    /// <summary>Ein Zeitgeber, der nichts tut; entsorgen hält einen schon laufenden Rückruf nicht auf.</summary>
    private sealed class InertTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
