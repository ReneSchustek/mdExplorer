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

    [ObservableProperty]
    private bool _loadRemoteImagesInPreview;

    /// <summary>Erzeugt das ViewModel mit den aktuellen Settings.</summary>
    public BehaviorTabViewModel(BehaviorSettings initial, UpdateSectionViewModel update)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(update);
        _searchDebounceMs = initial.SearchDebounceMs;
        _indexerResyncIntervalSeconds = initial.IndexerResyncIntervalSeconds;
        _checkForUpdatesAtStartup = initial.CheckForUpdatesAtStartup;
        _loadRemoteImagesInPreview = initial.LoadRemoteImagesInPreview;
        Update = update;
    }

    /// <summary>Update-Abschnitt: Prüfen und Installieren.</summary>
    public UpdateSectionViewModel Update { get; }

    /// <summary>Erzeugt das Settings-Record aus den aktuellen Eingaben.</summary>
    public BehaviorSettings ToSettings() =>
        new(SearchDebounceMs, IndexerResyncIntervalSeconds, CheckForUpdatesAtStartup, LoadRemoteImagesInPreview);
}
