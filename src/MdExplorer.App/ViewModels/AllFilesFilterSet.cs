using System.Linq;
using MdExplorer.App.Localization;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Die Einschränkungen der Datei-Liste: Suchtext, Ordner, Kennzeichnungen und Änderungszeitraum.
/// </summary>
/// <remarks>
/// Hält den Stand der Filter und entscheidet, ob ein Eintrag sie passiert. Die Liste selbst,
/// ihre Sortierung und ihr Anzeigezustand bleiben beim <see cref="AllFilesViewModel"/>.
/// </remarks>
internal sealed class AllFilesFilterSet
{
    /// <summary>Tage, die der Zeitraum „7 Tage" umfasst, einschließlich des laufenden.</summary>
    private const int SevenDayWindow = 7;

    /// <summary>Tage, die der Zeitraum „30 Tage" umfasst, einschließlich des laufenden.</summary>
    private const int ThirtyDayWindow = 30;

    private readonly TimeProvider _timeProvider;
    private readonly HashSet<string> _tags = new(StringComparer.OrdinalIgnoreCase);
    private string? _folder;

    /// <summary>Erzeugt einen Filtersatz ohne Einschränkung.</summary>
    /// <param name="timeProvider">Die Uhr, nach der „heute“ beginnt.</param>
    public AllFilesFilterSet(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <summary>Der gewählte Änderungszeitraum.</summary>
    public AllFilesPeriod Period { get; private set; } = AllFilesPeriod.Any;

    /// <summary>Schränkt auf einen Ordner ein, relativ zur Wurzel; leer steht für die Wurzel selbst.</summary>
    public void SetFolder(string folderPath) => _folder = folderPath;

    /// <summary>Nimmt eine Kennzeichnung hinzu.</summary>
    /// <returns><see langword="false"/>, wenn sie schon gesetzt war.</returns>
    public bool AddTag(string tagSlug) => _tags.Add(tagSlug);

    /// <summary>Wählt den Zeitraum.</summary>
    /// <returns><see langword="false"/>, wenn er schon gewählt war.</returns>
    public bool SetPeriod(AllFilesPeriod period)
    {
        if (Period == period)
        {
            return false;
        }
        Period = period;
        return true;
    }

    /// <summary>Nimmt den Filter zurück, für den ein Chip steht.</summary>
    /// <returns><see langword="false"/> für eine unbekannte Art.</returns>
    public bool Remove(ActiveFilterViewModel filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        switch (filter.Kind)
        {
            case AllFilesFilterKind.Folder:
                _folder = null;
                return true;
            case AllFilesFilterKind.Tag:
                _ = _tags.Remove(filter.Value);
                return true;
            case AllFilesFilterKind.Period:
                Period = AllFilesPeriod.Any;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Nimmt Ordner, Kennzeichnungen und Zeitraum zurück.</summary>
    public void Reset()
    {
        _folder = null;
        _tags.Clear();
        Period = AllFilesPeriod.Any;
    }

    /// <summary>Baut die Prüfung für einen Durchgang über die Liste.</summary>
    /// <remarks>
    /// Die Untergrenze des Zeitraums wird einmal je Durchgang berechnet, nicht je Eintrag: Sie
    /// fragt die Uhr und rechnet die Zeitzone um.
    /// </remarks>
    /// <param name="searchText">Der Suchtext; leer schränkt nicht ein.</param>
    /// <returns>Ob ein Eintrag Suche und Filter passiert.</returns>
    public Func<AllFilesItemViewModel, bool> CreateMatcher(string searchText)
    {
        string needle = searchText.Trim();
        DateTime earliestUtc = EarliestOf(Period);
        return item => (needle.Length == 0 || MatchesSearch(item, needle)) && MatchesFilters(item, earliestUtc);
    }

    /// <summary>Die Chips der wirkenden Filter, jeder einzeln entfernbar.</summary>
    /// <param name="periodFilters">Die Umschalter der Zeiträume; ihre Aufschrift erscheint im Chip.</param>
    public IEnumerable<ActiveFilterViewModel> Chips(IReadOnlyList<PeriodFilterViewModel> periodFilters)
    {
        if (_folder is not null)
        {
            string shown = _folder.Length == 0 ? UiText.Get("AllFiles_Filter_RootFolder") : _folder;
            yield return new ActiveFilterViewModel(AllFilesFilterKind.Folder, _folder, UiText.Format("AllFiles_Filter_Folder", shown));
        }

        foreach (string tag in _tags.Order(StringComparer.OrdinalIgnoreCase))
        {
            yield return new ActiveFilterViewModel(AllFilesFilterKind.Tag, tag, UiText.Format("AllFiles_Filter_Tag", tag));
        }

        if (Period != AllFilesPeriod.Any)
        {
            PeriodFilterViewModel chosen = periodFilters.First(filter => filter.Period == Period);
            yield return new ActiveFilterViewModel(AllFilesFilterKind.Period, chosen.Label, UiText.Format("AllFiles_Filter_Period", chosen.Label));
        }
    }

    private static bool MatchesSearch(AllFilesItemViewModel item, string needle) =>
        item.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || item.RelativePath.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || item.TagSlugs.Any(slug => slug.Contains(needle, StringComparison.OrdinalIgnoreCase));

    // Einige Zeitzonen stellen die Uhr um Mitternacht vor; dann gibt es 00:00 an diesem Tag
    // nicht, und die Umrechnung würfe. Der Tag beginnt dort mit der ersten gültigen Stunde.
    private static DateTime StartOfLocalDayUtc(DateTime localDate, TimeZoneInfo zone)
    {
        DateTime start = DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(start))
        {
            start = start.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(start, zone);
    }

    private bool MatchesFilters(AllFilesItemViewModel item, DateTime earliestUtc)
    {
        if (_folder is not null && !string.Equals(item.FolderPath, _folder, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Mehrere Kennzeichnungen wirken zusammen, nicht nebeneinander: Wer zwei anklickt,
        // erwartet die Schnittmenge, nicht die Summe.
        return _tags.All(tag => item.TagSlugs.Contains(tag, StringComparer.OrdinalIgnoreCase))
            && item.LastWriteTimeUtc >= earliestUtc;
    }

    /// <summary>Der früheste Zeitpunkt in UTC, der im gewählten Zeitraum noch mitzählt.</summary>
    /// <remarks>
    /// Die Tage sind die der Uhr neben dem Bildschirm, nicht die von UTC. Sonst begänne „Heute"
    /// in Deutschland im Sommer erst um zwei Uhr nachts, und was davor geändert wurde, fiele
    /// in den Vortag.
    /// </remarks>
    private DateTime EarliestOf(AllFilesPeriod period)
    {
        if (period == AllFilesPeriod.Any)
        {
            return DateTime.MinValue;
        }

        TimeZoneInfo zone = _timeProvider.LocalTimeZone;
        DateTime today = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), zone).Date;

        // Der laufende Tag zählt mit: „7 Tage" meint heute und die sechs davor, nicht acht.
        return period switch
        {
            AllFilesPeriod.Today => StartOfLocalDayUtc(today, zone),
            AllFilesPeriod.LastSevenDays => StartOfLocalDayUtc(today.AddDays(-(SevenDayWindow - 1)), zone),
            AllFilesPeriod.LastThirtyDays => StartOfLocalDayUtc(today.AddDays(-(ThirtyDayWindow - 1)), zone),
            _ => DateTime.MinValue,
        };
    }
}
