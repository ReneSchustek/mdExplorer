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
/// <see cref="SnapshotJson"/>-Property für die View. Der Pfad-Prefix-Filter, mit dem zuletzt
/// geladen wurde, wird im <see cref="IUiLayoutStore"/> gemerkt, damit das Fenster beim
/// Wiederöffnen in derselben Sicht startet.
/// </summary>
internal sealed partial class GraphViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IUiLayoutStore _settingsStore;
    private readonly ILogger<GraphViewModel> _logger;

    /// <summary>
    /// Der Merker, der beim Beenden der Anwendung anschlägt.
    /// </summary>
    /// <remarks>
    /// Er kommt aus dem Wirt (<c>IHostApplicationLifetime.ApplicationStopping</c>). Ohne ihn
    /// wartete das Schließen auf Arbeit, die niemand mehr braucht. Ohne Wirt, also im Test,
    /// bleibt er der leere Merker.
    /// </remarks>
    private readonly CancellationToken _shutdownToken;

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
        IUiLayoutStore settingsStore,
        ILogger<GraphViewModel> logger,
        CancellationToken shutdownToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _settingsStore = settingsStore;
        _logger = logger;
        _shutdownToken = shutdownToken;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        _pathPrefix = settingsStore.Load().GraphPathPrefix ?? string.Empty;
    }

    /// <summary>Lädt den Snapshot neu und aktualisiert die View.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// Lädt einen frischen Snapshot. Fehler landen im Protokoll; der zuletzt angezeigte
    /// Snapshot bleibt sichtbar, bis ein erfolgreicher Lauf ihn ersetzt. Ein Aufruf, während
    /// noch einer läuft, kehrt sofort zurück.
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
            string? prefix = string.IsNullOrWhiteSpace(PathPrefix) ? null : PathPrefix;
            RememberPrefix(prefix);
            GraphSnapshot snapshot = await BuildSnapshotAsync(new GraphFilter(prefix)).ConfigureAwait(true);
            Show(snapshot);
        }
        catch (OperationCanceledException)
        {
            // Die Anwendung wird beendet; das Bild sieht niemand mehr.
        }
        catch (Exception exception) when (DataLoadFailure.IsExpected(exception))
        {
            LogRefreshFailed(_logger, exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [LoggerMessage(EventId = 1200, Level = LogLevel.Warning, Message = "Graph-Snapshot konnte nicht aktualisiert werden.")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "Graph-Snapshot aktualisiert — {NodeCount} von {OriginalNodeCount} Knoten, {EdgeCount} von {OriginalEdgeCount} Kanten.")]
    private static partial void LogRefreshCompleted(ILogger logger, int nodeCount, int edgeCount, int originalNodeCount, int originalEdgeCount);

    private async Task<GraphSnapshot> BuildSnapshotAsync(GraphFilter filter)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(true))
        {
            IGraphService service = scope.ServiceProvider.GetRequiredService<IGraphService>();
            return await service.BuildSnapshotAsync(filter, _shutdownToken).ConfigureAwait(true);
        }
    }

    private void Show(GraphSnapshot snapshot)
    {
        NodeCount = snapshot.Nodes.Count;
        EdgeCount = snapshot.Edges.Count;
        OriginalNodeCount = snapshot.OriginalNodeCount;
        OriginalEdgeCount = snapshot.OriginalEdgeCount;
        SnapshotJson = GraphJsonBuilder.Serialize(snapshot);
        LogRefreshCompleted(_logger, NodeCount, EdgeCount, OriginalNodeCount, OriginalEdgeCount);
    }

    /// <summary>
    /// Merkt sich den Filter, mit dem geladen wird.
    /// </summary>
    /// <remarks>
    /// Beim Laden und nicht bei jedem Tastenanschlag: Das Eingabefeld meldet jedes Zeichen, und
    /// jedes Mal die Layout-Datei zu lesen und zu schreiben hielte die Oberfläche an. Gemerkt
    /// wird ohnehin die Sicht, die das Fenster zeigt, und die entsteht erst beim Laden.
    /// </remarks>
    private void RememberPrefix(string? prefix)
    {
        UiLayout current = _settingsStore.Load();
        if (string.Equals(current.GraphPathPrefix, prefix, StringComparison.Ordinal))
        {
            return;
        }
        _settingsStore.Save(current with { GraphPathPrefix = prefix });
    }
}
