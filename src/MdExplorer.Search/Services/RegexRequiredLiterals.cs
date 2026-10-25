using System.Text;

namespace MdExplorer.Search.Services;

/// <summary>
/// Liest aus einem regulären Ausdruck die Wortanfänge, die in jedem Treffer vorkommen müssen.
/// </summary>
/// <remarks>
/// <para>
/// Der Vorfilter der RegEx-Suche fragt FTS5 nach Wörtern, die mit einem Teil des Musters
/// beginnen. Das ist nur dann verlustfrei, wenn der Teil in jedem Treffer steht und dort an einem
/// Wortanfang beginnt. <c>ment</c> trifft „Dokument“ mitten im Wort; FTS5 fände das Dokument über
/// <c>"ment"*</c> nie, und der Treffer fehlte still.
/// </para>
/// <para>
/// Ein Wortanfang ist gesichert durch <c>^</c>, <c>\b</c>, <c>\s</c>, <c>\W</c> oder ein festes
/// Zeichen, das kein Wortzeichen ist (Leerzeichen, Bindestrich, Punkt). Der Anfang des Musters
/// allein genügt nicht. Alles, was nicht sicher in jedem Treffer steht, bricht ab: Gruppen,
/// Zeichenklassen, <c>.</c>, Alternativen mit <c>|</c> und Zeichen mit <c>?</c>, <c>*</c> oder
/// <c>{0,…}</c>. Ist das Muster im Modus <c>x</c> geschrieben, gelten Leerzeichen nicht; dann
/// liefert die Auswertung nichts.
/// </para>
/// </remarks>
internal static class RegexRequiredLiterals
{
    // Ein Buchstabe als Präfix träfe einen Großteil des Bestands; er bringt dem Vorfilter nichts.
    private const int MinLiteralLength = 2;

    // Ein Backslash und das Zeichen, das er maskiert.
    private const int EscapeSequenceLength = 2;

    /// <summary>Liefert die gesicherten Wortanfänge in Musterreihenfolge.</summary>
    /// <param name="pattern">Der reguläre Ausdruck des Nutzers.</param>
    /// <returns>Die Wortanfänge; leer, wenn keiner gesichert ist.</returns>
    public static List<string> Extract(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (HasTopLevelAlternation(pattern) || pattern.Contains("(?x", StringComparison.Ordinal))
        {
            return [];
        }

        Scanner scanner = new(pattern);
        scanner.Run();
        return scanner.Literals;
    }

    private static bool HasTopLevelAlternation(string pattern)
    {
        int depth = 0;
        bool inClass = false;
        int index = 0;
        while (index < pattern.Length)
        {
            char character = pattern[index];
            if (character == '\\')
            {
                index += EscapeSequenceLength;
                continue;
            }
            index++;
            if (inClass)
            {
                inClass = character != ']';
                continue;
            }
            if (character == '|' && depth == 0)
            {
                return true;
            }
            inClass = character == '[';
            depth += character switch { '(' => 1, ')' => -1, _ => 0 };
        }
        return false;
    }

    /// <summary>Ein Durchlauf über das Muster; sammelt gesicherte Wortanfänge.</summary>
    private sealed class Scanner(string pattern)
    {
        private readonly StringBuilder _run = new();

        private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';
        private int _index;
        private bool _boundaryBefore;
        private bool _runAnchored;

        public List<string> Literals { get; } = [];

        public void Run()
        {
            while (_index < pattern.Length)
            {
                Step(pattern[_index]);
            }
            EndRun();
        }

        private void Step(char character)
        {
            switch (character)
            {
                case '\\':
                    Escape();
                    break;
                case '^':
                    EndRun();
                    _boundaryBefore = true;
                    _index++;
                    break;
                case '(':
                    SkipBalanced('(', ')');
                    break;
                case '[':
                    SkipClass();
                    break;
                case '.' or '$':
                    EndRun();
                    _boundaryBefore = false;
                    _index++;
                    SkipQuantifier();
                    break;
                default:
                    Literal(character, isWordCharacter: IsWordCharacter(character));
                    break;
            }
        }

        private void Escape()
        {
            if (_index + 1 >= pattern.Length)
            {
                _index++;
                EndRun();
                return;
            }

            char escaped = pattern[_index + 1];
            _index += EscapeSequenceLength;
            if (char.IsLetterOrDigit(escaped))
            {
                // \b, \s, \W, \n, \t sichern eine Wortgrenze dahinter; \d, \w, \B und Rückverweise nicht.
                EndRun();
                _boundaryBefore = escaped is 'b' or 's' or 'W' or 'n' or 'r' or 't' or 'f' or 'v';
                SkipQuantifier();
                return;
            }

            // Maskiertes Sonderzeichen (\. \- \( …): ein festes Zeichen, kein Wortzeichen.
            _index--;
            Literal(escaped, isWordCharacter: false);
        }

        // _index steht auf dem Zeichen; der Quantor dahinter entscheidet, ob es sicher vorkommt.
        private void Literal(char character, bool isWordCharacter)
        {
            _index++;
            bool optional = IsOptionalQuantifier();
            bool repeated = !optional && IsRepeatingQuantifier();
            SkipQuantifier();

            if (!isWordCharacter)
            {
                EndRun();
                _boundaryBefore = !optional;
                return;
            }
            if (optional)
            {
                EndRun();
                _boundaryBefore = false;
                return;
            }
            if (_run.Length == 0)
            {
                _runAnchored = _boundaryBefore;
            }
            _ = _run.Append(character);
            _boundaryBefore = false;
            if (repeated)
            {
                EndRun();
            }
        }

        private bool IsOptionalQuantifier() =>
            _index < pattern.Length
            && (pattern[_index] is '?' or '*'
                || (pattern[_index] == '{' && _index + 1 < pattern.Length && pattern[_index + 1] == '0'));

        private bool IsRepeatingQuantifier() =>
            _index < pattern.Length && pattern[_index] is '+' or '{';

        private void SkipQuantifier()
        {
            if (_index >= pattern.Length)
            {
                return;
            }
            if (pattern[_index] == '{')
            {
                int close = pattern.IndexOf('}', _index);
                _index = close < 0 ? pattern.Length : close + 1;
            }
            else if (pattern[_index] is '?' or '*' or '+')
            {
                _index++;
            }

            // Ein nachgestelltes ? macht den Quantor nur genügsam, nicht optionaler.
            if (_index < pattern.Length && pattern[_index] == '?')
            {
                _index++;
            }
        }

        private void SkipBalanced(char open, char close)
        {
            EndRun();
            int depth = 0;
            while (_index < pattern.Length)
            {
                char character = pattern[_index];
                _index++;
                if (character == '\\')
                {
                    _index++;
                    continue;
                }
                if (character == '[')
                {
                    _index--;
                    SkipClassBody();
                    continue;
                }
                depth += character == open ? 1 : 0;
                depth -= character == close ? 1 : 0;
                if (depth == 0)
                {
                    break;
                }
            }
            _boundaryBefore = false;
            SkipQuantifier();
        }

        private void SkipClass()
        {
            EndRun();
            SkipClassBody();
            _boundaryBefore = false;
            SkipQuantifier();
        }

        // _index steht auf "["; danach steht er hinter dem schließenden "]".
        private void SkipClassBody()
        {
            _index++;
            if (_index < pattern.Length && pattern[_index] == ']')
            {
                _index++;
            }
            while (_index < pattern.Length && pattern[_index] != ']')
            {
                _index += pattern[_index] == '\\' ? EscapeSequenceLength : 1;
            }
            _index++;
        }

        private void EndRun()
        {
            if (_runAnchored && _run.Length >= MinLiteralLength)
            {
                Literals.Add(_run.ToString());
            }
            _ = _run.Clear();
            _runAnchored = false;
        }
    }
}
