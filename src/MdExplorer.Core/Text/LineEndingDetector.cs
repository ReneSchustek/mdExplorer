namespace MdExplorer.Core.Text;

/// <summary>
/// Erkennt das dominante Zeilenende einer Textdatei. Wird beim Laden einer
/// Markdown-Datei in den Editor aufgerufen, damit das Schreiben die ursprüngliche
/// Konvention erhält.
/// </summary>
public static class LineEndingDetector
{
    /// <summary>Länge einer CRLF-Sequenz (<c>\r\n</c>) in Zeichen.</summary>
    private const int CrlfLength = 2;

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

        CountLineEndings(text, out int crlf, out int loneLf, out int loneCr);
        if (crlf == 0 && loneLf == 0 && loneCr == 0)
        {
            return Default;
        }

        return ResolveDominant(crlf, loneLf, loneCr);
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

    /// <summary>Zählt CRLF-, einzelne LF- und einzelne CR-Vorkommen in einem Durchlauf.</summary>
    private static void CountLineEndings(string text, out int crlf, out int loneLf, out int loneCr)
    {
        crlf = 0;
        loneLf = 0;
        loneCr = 0;

        int index = 0;
        while (index < text.Length)
        {
            char current = text[index];
            if (current == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    crlf++;
                    index += CrlfLength;
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
    }

    /// <summary>Wählt das dominante Zeilenende; bei Gleichstand gewinnt CRLF vor LF vor CR.</summary>
    private static LineEndingStyle ResolveDominant(int crlf, int loneLf, int loneCr)
    {
        if (crlf >= loneLf && crlf >= loneCr)
        {
            return LineEndingStyle.Crlf;
        }
        return loneLf >= loneCr ? LineEndingStyle.Lf : LineEndingStyle.Cr;
    }
}
