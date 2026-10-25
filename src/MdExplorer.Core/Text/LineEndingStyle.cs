namespace MdExplorer.Core.Text;

/// <summary>
/// Zeilenende-Konvention einer Textdatei. Der Editor erkennt die Konvention beim Laden und
/// schreibt sie beim Speichern unverändert zurück; eine Datei aus einem Unix-Repository bleibt
/// so bei LF, statt nach dem ersten Speichern in jeder Zeile als geändert zu gelten.
/// </summary>
public enum LineEndingStyle
{
    /// <summary>Windows-Standard <c>\r\n</c>.</summary>
    Crlf,

    /// <summary>Unix-Standard <c>\n</c>.</summary>
    Lf,

    /// <summary>Klassischer Mac/Altsystem-Standard <c>\r</c>.</summary>
    Cr,
}
