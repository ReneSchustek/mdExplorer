using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// ViewModel des "Alle Dateien"-Tabs. Laedt die flache Liste indizierter
/// Markdown-Dateien inklusive ihrer Tag-Slugs ueber <see cref="IAllFilesQuery"/> und
/// filtert sie clientseitig nach Substring-Match in Titel, Pfad und Tags. Sortierung
/// und Tag-Klick (Filter-Token) sind UI-getrieben.
/// </summary>
internal sealed partial class AllFilesViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AllFilesViewModel> _logger;
    private AllFilesItemViewModel[] _allItems = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private AllFilesSortMode _sortMode = AllFilesSortMode.LastModified;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private AllFilesItemViewModel? _selectedItem;

    /// <summary>Wird ausgeloest, sobald ein Eintrag ausgewaehlt wird (mit absolutem Pfad).</summary>
    public event Action<string>? FileSelected;

    /// <summary>Erzeugt das ViewModel und verdrahtet die Refresh-Aktion.</summary>
    public AllFilesViewModel(IServiceScopeFactory scopeFactory, ILogger<AllFilesViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _logger = logger;
        Items = [];
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    /// <summary>Aktuelle (gefilterte + sortierte) Sicht auf die Datei-Liste.</summary>
    public ObservableCollection<AllFilesItemViewModel> Items { get; }

    /// <summary>Loest einen Lade-Roundtrip aus.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Laedt die flache Datei-Liste aus dem Indexer-Store.</summary>
    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        try
        {
            AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(true))
            {
                IAllFilesQuery query = scope.ServiceProvider.GetRequiredService<IAllFilesQuery>();
                IReadOnlyList<AllFilesRow> rows = await query.GetAllAsync(CancellationToken.None).ConfigureAwait(true);
                _allItems = rows.Select(row => new AllFilesItemViewModel(row)).ToArray();
                ApplyViewState();
                LogLoaded(_logger, _allItems.Length);
            }
        }
        catch (InvalidOperationException ex)
        {
            LogRefreshFailed(_logger, ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyViewState();
    }

    partial void OnSortModeChanged(AllFilesSortMode value)
    {
        ApplyViewState();
    }

    partial void OnSelectedItemChanged(AllFilesItemViewModel? value)
    {
        if (value is null)
        {
            return;
        }
        FileSelected?.Invoke(value.AbsolutePath);
    }

    private void ApplyViewState()
    {
        string trimmed = SearchText?.Trim() ?? string.Empty;
        IEnumerable<AllFilesItemViewModel> filtered = string.IsNullOrEmpty(trimmed)
            ? _allItems
            : _allItems.Where(item => MatchesSearch(item, trimmed));

        IEnumerable<AllFilesItemViewModel> sorted = SortMode switch
        {
            AllFilesSortMode.Title => filtered.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase),
            AllFilesSortMode.RelativePath => filtered.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderByDescending(item => item.LastWriteTimeUtc).ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase),
        };

        Items.Clear();
        foreach (AllFilesItemViewModel item in sorted)
        {
            Items.Add(item);
        }
    }

    private static bool MatchesSearch(AllFilesItemViewModel item, string needle)
    {
        if (item.Title.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (item.RelativePath.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return item.TagSlugs.Any(slug => slug.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    [LoggerMessage(EventId = 1300, Level = LogLevel.Information, Message = "Alle-Dateien-Tab geladen — {Count} Eintraege.")]
    private static partial void LogLoaded(ILogger logger, int count);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "Alle-Dateien-Tab konnte nicht geladen werden.")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);
}

/// <summary>Sortier-Modi fuer die Datei-Liste.</summary>
internal enum AllFilesSortMode
{
    /// <summary>Standard: nach Schreibdatum absteigend.</summary>
    LastModified = 0,

    /// <summary>Nach Dateiname (Titel) aufsteigend.</summary>
    Title = 1,

    /// <summary>Nach relativem Pfad aufsteigend.</summary>
    RelativePath = 2,
}

/// <summary>Item-View fuer einen Datei-Eintrag im Alle-Dateien-Tab.</summary>
internal sealed class AllFilesItemViewModel
{
    /// <summary>Erzeugt einen Eintrag aus einer Query-Zeile.</summary>
    public AllFilesItemViewModel(AllFilesRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        MarkdownFileId = row.MarkdownFileId;
        Title = row.Title;
        RelativePath = row.RelativePath;
        AbsolutePath = row.AbsolutePath;
        LastWriteTimeUtc = row.LastWriteTimeUtc;
        TagSlugs = row.TagSlugs;
    }

    /// <summary>Stabiler Schluessel.</summary>
    public Guid MarkdownFileId { get; }

    /// <summary>Dateiname ohne Erweiterung.</summary>
    public string Title { get; }

    /// <summary>Pfad relativ zum konfigurierten Root.</summary>
    public string RelativePath { get; }

    /// <summary>Vollqualifizierter Pfad — Eingabe fuer den Navigations-Locator.</summary>
    public string AbsolutePath { get; }

    /// <summary>Letzte Aenderung auf Disk (UTC).</summary>
    public DateTime LastWriteTimeUtc { get; }

    /// <summary>Slugs der angewendeten Tags.</summary>
    public IReadOnlyList<string> TagSlugs { get; }
}
