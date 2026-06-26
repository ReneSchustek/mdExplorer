using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Services;
using MdExplorer.Graph.Abstractions;
using MdExplorer.Graph.Models;
using MdExplorer.Graph.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels.Graph;

/// <summary>
/// ViewModel des Graph-Panels. Lädt den Snapshot über einen eigenen DI-Scope
/// (<see cref="IServiceScopeFactory"/>), serialisiert ihn via
/// <see cref="GraphJsonBuilder"/> und exponiert das Ergebnis als
/// <see cref="SnapshotJson"/>-Property für die View. Der Pfad-Prefix-Filter wird
/// in <see cref="UiSettingsStore"/> persistiert, damit das Fenster beim Wiederöffnen
/// in derselben Sicht startet.
/// </summary>
internal sealed partial class GraphViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly UiSettingsStore _settingsStore;
    private readonly ILogger<GraphViewModel> _logger;
    private bool _suppressPrefixSave;

    [ObservableProperty]
    private string? _snapshotJson;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _nodeCount;

    [ObservableProperty]
    private int _edgeCount;

    [ObservableProperty]
    private int _originalNodeCount;

    [ObservableProperty]
    private int _originalEdgeCount;

    [ObservableProperty]
    private string _pathPrefix = string.Empty;

    /// <summary>Erzeugt das ViewModel und löst Pflichtabhängigkeiten auf.</summary>
    public GraphViewModel(
        IServiceScopeFactory scopeFactory,
        UiSettingsStore settingsStore,
        ILogger<GraphViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _settingsStore = settingsStore;
        _logger = logger;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);

        _suppressPrefixSave = true;
        _pathPrefix = settingsStore.Load().GraphPathPrefix ?? string.Empty;
        _suppressPrefixSave = false;
    }

    /// <summary>Lädt den Snapshot neu und aktualisiert die View.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// Lädt asynchron einen frischen Snapshot. Fehler werden geloggt; das
    /// vorher angezeigte Snapshot bleibt sichtbar, bis ein erfolgreicher Lauf
    /// es ersetzt.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        try
        {
            GraphFilter filter = new(string.IsNullOrWhiteSpace(PathPrefix) ? null : PathPrefix);
            AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(true))
            {
                IGraphService service = scope.ServiceProvider.GetRequiredService<IGraphService>();
                GraphSnapshot snapshot = await service.BuildSnapshotAsync(filter, CancellationToken.None).ConfigureAwait(true);
                NodeCount = snapshot.Nodes.Count;
                EdgeCount = snapshot.Edges.Count;
                OriginalNodeCount = snapshot.OriginalNodeCount;
                OriginalEdgeCount = snapshot.OriginalEdgeCount;
                SnapshotJson = GraphJsonBuilder.Serialize(snapshot);
                LogRefreshCompleted(_logger, NodeCount, EdgeCount, OriginalNodeCount, OriginalEdgeCount);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
        {
            LogRefreshFailed(_logger, ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnPathPrefixChanged(string value)
    {
        if (_suppressPrefixSave)
        {
            return;
        }
        UiLayout current = _settingsStore.Load();
        string? normalized = string.IsNullOrWhiteSpace(value) ? null : value;
        if (string.Equals(current.GraphPathPrefix, normalized, StringComparison.Ordinal))
        {
            return;
        }
        _settingsStore.Save(current with { GraphPathPrefix = normalized });
    }

    [LoggerMessage(EventId = 1200, Level = LogLevel.Warning, Message = "Graph-Snapshot konnte nicht aktualisiert werden.")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "Graph-Snapshot aktualisiert — {NodeCount} von {OriginalNodeCount} Knoten, {EdgeCount} von {OriginalEdgeCount} Kanten.")]
    private static partial void LogRefreshCompleted(ILogger logger, int nodeCount, int edgeCount, int originalNodeCount, int originalEdgeCount);
}
