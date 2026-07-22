using System;

namespace MdExplorer.App.Services;

/// <summary>
/// Reine Find-Algorithmen fuer den Editor-Textbox (Ctrl+F). Stateless, damit der
/// Code unter Test stehen kann ohne WPF-Abhaengigkeit. Die UI bleibt im Code-Behind und
/// uebernimmt nur das Lesen/Schreiben von <c>SelectionStart</c> / <c>SelectionLength</c>.
/// </summary>
internal static class EditorFindHelper
{
    /// <summary>Position eines Treffers im Quelltext.</summary>
    /// <param name="StartIndex">Nullbasierter Start-Index des Treffers.</param>
    /// <param name="Length">Treffer-Laenge in Zeichen.</param>
    internal readonly record struct FindMatch(int StartIndex, int Length);

    /// <summary>Sucht das naechste Vorkommen von <paramref name="query"/> in <paramref name="text"/>
    /// ab <paramref name="startIndex"/>. Wenn ab dort kein Treffer existiert, wird vom Anfang wieder gesucht
    /// (Wrap-Around). Liefert <see langword="null"/>, wenn der Suchstring leer ist oder kein Treffer existiert.</summary>
    public static FindMatch? FindNext(string text, string query, int startIndex, bool caseSensitive = false)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
        {
            return null;
        }
        int clampedStart = Math.Clamp(startIndex, 0, text.Length);
        StringComparison comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int hit = text.IndexOf(query, clampedStart, comparison);
        if (hit < 0 && clampedStart > 0)
        {
            hit = text.IndexOf(query, 0, comparison);
        }
        return hit < 0 ? null : new FindMatch(hit, query.Length);
    }

    /// <summary>Sucht das vorhergehende Vorkommen von <paramref name="query"/> vor <paramref name="endIndex"/>.
    /// Bei keinem Treffer Wrap-Around vom Textende. Liefert <see langword="null"/> bei leerem
    /// Suchstring oder fehlendem Treffer.</summary>
    public static FindMatch? FindPrevious(string text, string query, int endIndex, bool caseSensitive = false)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
        {
            return null;
        }
        StringComparison comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        // LastIndexOf erwartet einen Start-Index vom Ende her — wir suchen "vor" endIndex,
        // also bis endIndex - 1 inklusive. Wenn endIndex <= 0, direkt Wrap-Around.
        int boundary = Math.Min(endIndex, text.Length);
        int hit = boundary > 0 ? text.LastIndexOf(query, boundary - 1, boundary, comparison) : -1;
        if (hit < 0 && boundary < text.Length)
        {
            hit = text.LastIndexOf(query, text.Length - 1, text.Length, comparison);
        }
        return hit < 0 ? null : new FindMatch(hit, query.Length);
    }
}
