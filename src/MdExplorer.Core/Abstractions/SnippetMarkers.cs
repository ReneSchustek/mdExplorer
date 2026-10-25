namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Die Zeichen, mit denen der Volltext-Ausschnitt seine Trefferstellen einfasst.
/// </summary>
/// <remarks>
/// Zwei Zeichen aus dem Privatbereich von Unicode statt einer HTML-Auszeichnung wie
/// <c>&lt;mark&gt;</c>: Stünde die Auszeichnung wörtlich in einer Notiz, verrutschten die
/// Hervorhebungen. Diese Zeichen kommen in Text praktisch nicht vor. SQLite erzeugt sie in der
/// Abfrage über <c>char(57344)</c> und <c>char(57345)</c>.
/// </remarks>
public static class SnippetMarkers
{
    /// <summary>Beginn einer Trefferstelle (U+E000).</summary>
    public const string Open = "";

    /// <summary>Ende einer Trefferstelle (U+E001).</summary>
    public const string Close = "";
}
