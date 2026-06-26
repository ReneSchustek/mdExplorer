using CommunityToolkit.Mvvm.ComponentModel;
using MdExplorer.Core.Models;

namespace MdExplorer.App.ViewModels.Settings;

/// <summary>
/// ViewModel für den Tab „Verhalten" — Such-Debounce und Indexer-Resync-Intervall.
/// </summary>
internal sealed partial class BehaviorTabViewModel : ObservableObject
{
    [ObservableProperty]
    private int _searchDebounceMs;

    [ObservableProperty]
    private int _indexerResyncIntervalSeconds;

    [ObservableProperty]
    private bool _checkForUpdatesAtStartup;

    /// <summary>Erzeugt das ViewModel mit den aktuellen Settings.</summary>
    public BehaviorTabViewModel(BehaviorSettings initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _searchDebounceMs = initial.SearchDebounceMs;
        _indexerResyncIntervalSeconds = initial.IndexerResyncIntervalSeconds;
        _checkForUpdatesAtStartup = initial.CheckForUpdatesAtStartup;
    }

    /// <summary>Erzeugt das Settings-Record aus den aktuellen Eingaben.</summary>
    public BehaviorSettings ToSettings() => new(SearchDebounceMs, IndexerResyncIntervalSeconds, CheckForUpdatesAtStartup);
}
