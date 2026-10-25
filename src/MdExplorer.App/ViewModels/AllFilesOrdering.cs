using System.Globalization;
using System.Linq;
using MdExplorer.Core.Text;

namespace MdExplorer.App.ViewModels;

/// <summary>Ordnung und Gruppenbuchstaben der Datei-Liste je Sortierung.</summary>
internal static class AllFilesOrdering
{
    /// <summary>Ob nach einem Text sortiert wird, sodass Buchstabengruppen einen Sinn haben.</summary>
    public static bool IsAlphabetical(AllFilesSortMode mode) =>
        mode is AllFilesSortMode.Title or AllFilesSortMode.RelativePath;

    /// <summary>Ordnet die Einträge; nach Datum stehen die neuesten oben, bei Gleichstand nach Pfad.</summary>
    public static IEnumerable<AllFilesItemViewModel> Sort(IEnumerable<AllFilesItemViewModel> items, AllFilesSortMode mode) => mode switch
    {
        AllFilesSortMode.Title => items.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase),
        AllFilesSortMode.RelativePath => items.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase),
        _ => items.OrderByDescending(item => item.LastWriteTimeUtc).ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>
    /// Der Gruppenbuchstabe eines Eintrags.
    /// </summary>
    /// <remarks>
    /// Er folgt dem Schlüssel, nach dem sortiert wurde. Nach Datum sortiert gibt es keinen
    /// sinnvollen Buchstaben; dann bleibt die Beschriftung leer und die Ansicht zeigt keinen
    /// Gruppenkopf.
    /// </remarks>
    public static string GroupLabelOf(AllFilesItemViewModel item, AllFilesSortMode mode) =>
        IsAlphabetical(mode) ? LetterOf(item, mode).ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>Die Buchstaben, unter denen in dieser Sortierung Einträge stehen.</summary>
    public static HashSet<char> OccupiedLetters(IEnumerable<AllFilesItemViewModel> items, AllFilesSortMode mode) =>
        IsAlphabetical(mode) ? [.. items.Select(item => LetterOf(item, mode))] : [];

    private static char LetterOf(AllFilesItemViewModel item, AllFilesSortMode mode) =>
        AlphabetIndex.LetterOf(mode == AllFilesSortMode.RelativePath ? item.RelativePath : item.Title);
}
