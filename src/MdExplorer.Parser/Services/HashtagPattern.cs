using System.Linq;
using System.Text.RegularExpressions;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Die eine Regel, was ein Hashtag ist.
/// </summary>
/// <remarks>
/// <para>
/// Drei Stellen brauchen sie: der Extractor, der Rewriter und der Dokument-Editor. Stünde
/// sie an jeder einzeln, wiche eine davon früher oder später ab: Kennt der Extractor keine
/// abschließende Bedingung, der Rewriter aber schon, wird bei <c>#notizé</c> <c>notiz</c>
/// indiziert und nichts umbenannt. Datei und Index liefen auseinander, ohne Meldung.
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
    /// Vorne kein Wortzeichen und kein zweites <c>#</c>, sonst würde jede Raute mitten im
    /// Wort zählen. Hinten kein Wortzeichen und kein Bindestrich, sonst bliebe bei einer
    /// Umbenennung der Rest des Wortes stehen. Als <c>const</c>, damit ihn Extractor und Rewriter
    /// dem Quelltext-Erzeuger von <see cref="GeneratedRegexAttribute"/> übergeben können.
    /// </remarks>
    public const string Expression =
        @"(?<![\w#])#(?<name>" + NameExpression + @")(?![\w-])";

    /// <summary>Ausdruck für den Namen allein, ohne Raute und ohne Grenzen.</summary>
    /// <remarks>
    /// Erst ein Buchstabe, dann Buchstaben, Ziffern, Unterstrich und Bindestrich. Kombinierende
    /// Zeichen (<c>\p{Mn}</c>) gehören dazu: Dateien von macOS speichern „ä“ oft als „a“ mit
    /// nachgestellten Punkten, und ohne sie endete der Name mitten im Wort.
    /// </remarks>
    public const string NameExpression = @"[A-Za-zÄÖÜäöüß][A-Za-z0-9ÄÖÜäöüß_\-\p{Mn}]+";

    /// <summary>
    /// Zeitgrenze in Millisekunden für einen einzelnen Lauf jedes Hashtag-Ausdrucks.
    /// </summary>
    /// <remarks>
    /// Der Ausdruck kennt keine Verschachtelung und kann nicht katastrophal zurücksetzen. Die
    /// Grenze gilt trotzdem für jeden Ausdruck mit diesem Muster, auch für die aus dem
    /// Quelltext-Erzeuger: Sie kostet nichts und macht den Fall unmöglich, dass eine spätere
    /// Änderung am Muster genau das einführt.
    /// </remarks>
    public const int MatchTimeoutMilliseconds = 1000;

    /// <summary>
    /// Zulässige Längen eines Farbwerts in Hexadezimalschreibweise, wie CSS sie kennt:
    /// <c>#RGB</c>, <c>#RGBA</c>, <c>#RRGGBB</c> und <c>#RRGGBBAA</c>.
    /// </summary>
    private static readonly int[] ColorLiteralLengths = [3, 4, 6, 8];

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(MatchTimeoutMilliseconds);

    /// <summary>
    /// Baut den Ausdruck für ein einzelnes Schlagwort, mit gleichen Grenzen und festem Namen.
    /// </summary>
    /// <param name="tagName">Der Name ohne führende Raute.</param>
    /// <returns>Ein Ausdruck, der genau dieses Schlagwort trifft.</returns>
    public static Regex ForTag(string tagName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);

        return new Regex(
            $@"(?<![\w#])#{Regex.Escape(tagName)}(?![\w\-])",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            MatchTimeout);
    }

    /// <summary>
    /// Sagt, ob <paramref name="name"/> als Schlagwort im Text stehen kann.
    /// </summary>
    /// <remarks>
    /// Ein neuer Name beim Umbenennen muss diese Regel erfüllen. Mit Leerzeichen zerrisse er im Text
    /// den Hashtag („#neuer name“ zählt nur „neuer“), mit Komma oder Klammer machte er die
    /// <c>tags</c>-Zeile ungültig, und das ganze Frontmatter fiele aus dem Index.
    /// </remarks>
    /// <param name="name">Der Name, mit oder ohne führende Raute.</param>
    /// <returns><see langword="true"/>, wenn der Name vollständig auf <see cref="NameExpression"/> passt.</returns>
    public static bool IsValidName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return NameRegex().IsMatch(name.TrimStart('#'));
    }

    /// <summary>
    /// Sagt, ob ein Treffer in Wahrheit ein Farbwert ist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>#F59E0B</c> erfüllt jede Bedingung eines Schlagworts: erstes Zeichen ein Buchstabe,
    /// der Rest erlaubt. Ohne diese Prüfung landete jeder Farbwert einer Gestaltungsdokumentation
    /// als Schlagwort im Index, derselbe Farbwert oft an vielen Dateien zugleich.
    /// </para>
    /// <para>
    /// Das hat einen Preis: Ein gewolltes <c>#facade</c> oder <c>#b2b</c> besteht ebenfalls aus
    /// lauter Hexziffern und fällt mit weg. In deutschsprachiger Dokumentation ist das die seltenere
    /// Sorte Schlagwort. Wer die Entscheidung zurücknehmen will, tut es an der Längenliste.
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

    [GeneratedRegex("^" + NameExpression + "$", RegexOptions.CultureInvariant, MatchTimeoutMilliseconds)]
    private static partial Regex NameRegex();
}
