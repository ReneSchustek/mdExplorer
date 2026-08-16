using System.Linq;
using System.Text.RegularExpressions;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Die eine Regel, was ein Hashtag ist.
/// </summary>
/// <remarks>
/// <para>
/// Sie stand bis zum 16.08.2026 an drei Stellen — im Extractor, im Rewriter und im
/// Dokument-Editor — und an einer davon anders. Der Extractor kannte keine abschließende
/// Bedingung, der Rewriter schon. Bei <c>#notizé</c> hieß das: indiziert wurde
/// <c>notiz</c>, umbenannt wurde nichts. Datei und Index liefen auseinander, ohne Meldung.
/// </para>
/// <para>
/// Deshalb hier und nur hier. Wer die Regel ändert, ändert sie für alle drei.
/// </para>
/// </remarks>
public static partial class HashtagPattern
{
    /// <summary>
    /// Vollständiger Ausdruck mit benannter Gruppe <c>name</c>.
    /// </summary>
    /// <remarks>
    /// Vorne kein Wortzeichen und kein zweites <c>#</c> — sonst würde jede Raute mitten im
    /// Wort zählen. Hinten kein Wortzeichen und kein Bindestrich — sonst bliebe bei einer
    /// Umbenennung der Rest des Wortes stehen. Als <c>const</c>, damit ihn beide Module dem
    /// Quelltext-Erzeuger von <see cref="GeneratedRegexAttribute"/> übergeben können.
    /// </remarks>
    public const string Expression =
        @"(?<![\w#])#(?<name>[A-Za-zÄÖÜäöüß][A-Za-z0-9ÄÖÜäöüß_\-]+)(?![\w-])";

    /// <summary>Zulässige Längen eines Farbwerts in Hexadezimalschreibweise.</summary>
    private static readonly int[] ColorLiteralLengths = [3, 4, 6, 8];

    /// <summary>
    /// Baut den Ausdruck für ein einzelnes Schlagwort — gleiche Grenzen, fester Name.
    /// </summary>
    /// <param name="tagName">Der Name ohne führende Raute.</param>
    /// <returns>Ein Ausdruck, der genau dieses Schlagwort trifft.</returns>
    public static Regex ForTag(string tagName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);

        return new Regex(
            $@"(?<![\w#])#{Regex.Escape(tagName)}(?![\w\-])",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Sagt, ob ein Treffer in Wahrheit ein Farbwert ist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>#F59E0B</c> erfüllt jede Bedingung eines Schlagworts: erstes Zeichen ein Buchstabe,
    /// der Rest erlaubt. Der Index trug deshalb 42 solcher „Schlagworte", eines davon an
    /// 21 Dateien — und getroffen hat es ausgerechnet die Dokumentation der Gestaltungslinie.
    /// </para>
    /// <para>
    /// Der Preis ist zu nennen und nicht zu verschweigen: Ein gewolltes <c>#facade</c> oder
    /// <c>#b2b</c> besteht ebenfalls aus lauter Hexziffern und fällt mit weg. In
    /// deutschsprachiger Dokumentation ist das die seltenere Sorte Schlagwort; die Längenliste
    /// steht deshalb hier und ist der Ort, an dem man die Entscheidung zurücknimmt.
    /// </para>
    /// </remarks>
    /// <param name="candidate">Der Name ohne führende Raute.</param>
    /// <returns><see langword="true"/>, wenn der Name als Farbwert zu lesen ist.</returns>
    public static bool IsColorLiteral(string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return Array.IndexOf(ColorLiteralLengths, candidate.Length) >= 0
            && candidate.All(Uri.IsHexDigit);
    }
}
