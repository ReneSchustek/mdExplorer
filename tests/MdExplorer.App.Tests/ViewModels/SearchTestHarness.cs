using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Models;
using MdExplorer.Search.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Hält Suchdienst, Uhr, Nachrichtenverteiler und das Such-ViewModel für einen Testlauf zusammen.
/// </summary>
/// <remarks>
/// Die Uhr ist gestellt: <see cref="Type"/> setzt den Text und lässt die Entprellzeit ablaufen.
/// Die Attrappe des Suchdienstes antwortet sofort, und der Dispatcher führt sofort aus; die ganze
/// Kette läuft deshalb innerhalb von <see cref="FakeTimeProvider.Advance"/> durch. Kein Test wartet
/// auf die Wanduhr, und keiner hängt davon ab, wie schnell drei Zuweisungen hintereinander sind.
/// </remarks>
internal sealed class SearchTestHarness : IDisposable
{
    /// <summary>Die Entprellzeit aus den Einstellungen des Prüfstands.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    /// <summary>Die Seitengröße aus den Einstellungen des Prüfstands.</summary>
    public const int ResultsPerPage = 50;

    private readonly ServiceProvider _provider;

    public SearchTestHarness(ISearchService? searchService = null, IUiDispatcher? dispatcher = null)
    {
        SearchService = searchService ?? new FakeSearchService();
        ServiceCollection services = new();
        _ = services.AddScoped(_ => SearchService);
        _provider = services.BuildServiceProvider(validateScopes: true);
        Messenger = new StrongReferenceMessenger();
        ViewModel = new SearchViewModel(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Clock,
            Messenger,
            new FakeSettingsService(Settings(Debounce, ResultsPerPage)),
            dispatcher ?? new ImmediateUiDispatcher(),
            NullLogger<SearchViewModel>.Instance);
    }

    public ISearchService SearchService { get; }

    /// <summary>Der Suchdienst als zählende Attrappe, wenn keiner vorgegeben wurde.</summary>
    public FakeSearchService Recording => (FakeSearchService)SearchService;

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));

    public StrongReferenceMessenger Messenger { get; }

    public SearchViewModel ViewModel { get; }

    /// <summary>Die Einstellungen mit gegebener Entprellzeit und Seitengröße.</summary>
    public static AppSettings Settings(TimeSpan debounce, int resultsPerPage) => AppSettings.Default with
    {
        Behavior = AppSettings.Default.Behavior with { SearchDebounceMs = (int)debounce.TotalMilliseconds },
        Appearance = AppSettings.Default.Appearance with { ResultsPerPage = resultsPerPage },
    };

    /// <summary>Tippt <paramref name="text"/> und lässt die Entprellzeit ablaufen.</summary>
    public void Type(string text)
    {
        ViewModel.QueryText = text;
        Clock.Advance(Debounce);
    }

    /// <summary>Lässt die Entprellzeit ablaufen, etwa nach einem Wechsel des Modus.</summary>
    public void Elapse() => Clock.Advance(Debounce);

    public void Dispose()
    {
        ViewModel.Dispose();
        _provider.Dispose();
    }
}
