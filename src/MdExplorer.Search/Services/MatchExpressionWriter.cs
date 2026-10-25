using System.Text;

namespace MdExplorer.Search.Services;

/// <summary>
/// Setzt die MATCH-Expression so zusammen, dass FTS5 sie in jedem Zwischenstand annimmt.
/// </summary>
/// <remarks>
/// <para>
/// FTS5 kennt <c>NOT</c> nur zwischen zwei Ausdrücken. Ein ausgeschlossener Begriff
/// (<c>-entwurf</c>) wird deshalb nicht an seiner Stelle geschrieben, sondern gesammelt und
/// hinter den übrigen Ausdruck gestellt: <c>-entwurf bericht</c> wird zu
/// <c>"bericht" NOT "entwurf"</c>. Bleibt kein anderer Begriff, entsteht keine Anfrage; einen
/// Ausschluss ohne etwas, wovon ausgeschlossen wird, kann FTS5 nicht ausdrücken.
/// </para>
/// <para>
/// Enthält der übrige Ausdruck ein <c>OR</c>, kommt er in Klammern. <c>NOT</c> bindet in FTS5
/// stärker als <c>OR</c>; ohne Klammer schlösse der Begriff nur den letzten Zweig aus.
/// </para>
/// <para>
/// Ein Operator ohne linken Partner fällt weg, ebenso ein Operator am Ende, dem kein Begriff
/// mehr folgt. Beides entsteht beim Tippen ständig, etwa mit <c>bericht OR</c>.
/// </para>
/// </remarks>
internal sealed class MatchExpressionWriter
{
    private readonly StringBuilder _included = new();
    private readonly List<string> _excluded = [];
    private QueryTokenType? _lastEmitted;
    private int _openOperatorStart = -1;
    private bool _containsOr;

    /// <summary>Hängt einen Begriff an, eingeschlossen oder ausgeschlossen.</summary>
    /// <param name="fragment">Der fertig quotierte Begriff.</param>
    /// <param name="excluded">Ob der Begriff ausgeschlossen wird.</param>
    public void AddTerm(string fragment, bool excluded)
    {
        if (fragment.Length == 0)
        {
            return;
        }
        if (excluded)
        {
            _excluded.Add(fragment);
            return;
        }

        if (_lastEmitted is QueryTokenType.Term)
        {
            _ = _included.Append(" AND ");
        }
        else if (_lastEmitted is QueryTokenType.OperatorOr)
        {
            _containsOr = true;
        }
        _ = _included.Append(fragment);
        _lastEmitted = QueryTokenType.Term;
        _openOperatorStart = -1;
    }

    /// <summary>Hängt einen Ausdruck an, der selbst ein <c>OR</c> enthält, geklammert.</summary>
    /// <param name="expression">Der Ausdruck.</param>
    public void AddGroup(string expression) => AddTerm($"({expression})", excluded: false);

    /// <summary>Hängt einen Operator an; ohne linken Begriff fällt er weg.</summary>
    /// <param name="type">Einer der drei Operator-Typen.</param>
    public void AddOperator(QueryTokenType type)
    {
        if (_lastEmitted is not QueryTokenType.Term)
        {
            return;
        }

        string keyword = type switch
        {
            QueryTokenType.OperatorOr => "OR",
            QueryTokenType.OperatorNot => "NOT",
            _ => "AND",
        };
        _openOperatorStart = _included.Length;
        _ = _included.Append(' ').Append(keyword).Append(' ');
        _lastEmitted = type;
    }

    /// <summary>Liefert den fertigen Ausdruck; leer, wenn kein eingeschlossener Begriff bleibt.</summary>
    /// <returns>Die MATCH-Expression.</returns>
    public string ToMatchExpression()
    {
        if (_openOperatorStart >= 0)
        {
            _included.Length = _openOperatorStart;
        }
        if (_included.Length == 0)
        {
            return string.Empty;
        }

        string included = _included.ToString();
        if (_excluded.Count == 0)
        {
            return included;
        }

        StringBuilder expression = new(_containsOr ? $"({included})" : included);
        foreach (string fragment in _excluded)
        {
            _ = expression.Append(" NOT ").Append(fragment);
        }
        return expression.ToString();
    }
}
