namespace MdExplorer.Core.Text;

/// <summary>
/// Erkennt das dominante Zeilenende einer Textdatei. Wird beim Laden einer
/// Markdown-Datei in den Editor aufgerufen, damit das Schreiben die ursprüngliche
/// Konvention erhält.
/// </summary>
public static class LineEndingDetector
{
    /// <summary>Standard-Konvention für neu angelegte Dateien (Windows-Build → CRLF).</summary>
    public static LineEndingStyle Default => Environment.NewLine == "\n" ? LineEndingStyle.Lf : LineEndingStyle.Crlf;

    /// <summary>
    /// Liefert das in <paramref name="text"/> häufigste Zeilenende.
    /// Bei Gleichstand gewinnt CRLF (gemischte Dateien bekommen Windows-Konvention).
    /// Enthält der Text keine Zeilenumbrüche, wird <see cref="Default"/> zurückgegeben.
    /// </summary>
    public static LineEndingStyle Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int crlf = 0;
        int loneLf = 0;
        int loneCr = 0;

        int index = 0;
        while (index < text.Length)
        {
            char current = text[index];
            if (current == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    crlf++;
                    index += 2;
                    continue;
                }
                loneCr++;
            }
            else if (current == '\n')
            {
                loneLf++;
            }
            index++;
        }

        if (crlf == 0 && loneLf == 0 && loneCr == 0)
        {
            return Default;
        }

        if (crlf >= loneLf && crlf >= loneCr)
        {
            return LineEndingStyle.Crlf;
        }
        return loneLf >= loneCr ? LineEndingStyle.Lf : LineEndingStyle.Cr;
    }

    /// <summary>Liefert die Token-Form (<c>\r\n</c>, <c>\n</c>, <c>\r</c>) eines <see cref="LineEndingStyle"/>.</summary>
    public static string ToToken(LineEndingStyle style) => style switch
    {
        LineEndingStyle.Crlf => "\r\n",
        LineEndingStyle.Lf => "\n",
        LineEndingStyle.Cr => "\r",
        _ => "\r\n",
    };

    /// <summary>
    /// Wandelt alle Zeilenumbrüche in <paramref name="text"/> einheitlich auf <paramref name="style"/>.
    /// Roundtrip-fähig: erst alle CRLF → LF, dann LF/CR → Ziel-Token. Damit bleibt die Reihenfolge stabil.
    /// </summary>
    public static string Normalize(string text, LineEndingStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);

        string canonical = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string token = ToToken(style);
        return token == "\n" ? canonical : canonical.Replace("\n", token, StringComparison.Ordinal);
    }
}
