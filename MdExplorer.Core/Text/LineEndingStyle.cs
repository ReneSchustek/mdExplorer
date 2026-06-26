namespace MdExplorer.Core.Text;

/// <summary>
/// Zeilenende-Konvention einer Textdatei. Beim Editieren wird die ursprüngliche
/// Konvention der Datei erkannt und beim Speichern unverändert zurückgeschrieben,
/// damit MdExplorer keine plattformfremden Markdown-Dateien um-formatiert.
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
