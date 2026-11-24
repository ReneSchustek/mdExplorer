using System.Text;

namespace MdExplorer.Parser.Services;

/// <summary>Eine Ersetzung im Text: der Bereich ab <see cref="Start"/> mit <see cref="Length"/> Zeichen wird zu <see cref="Replacement"/>.</summary>
/// <param name="Start">Erstes ersetztes Zeichen.</param>
/// <param name="Length">Anzahl ersetzter Zeichen.</param>
/// <param name="Replacement">Der neue Text.</param>
internal readonly record struct TextEdit(int Start, int Length, string Replacement)
{
    /// <summary>Wendet alle Ersetzungen an; sie dürfen sich nicht überlappen.</summary>
    /// <param name="text">Der Ausgangstext.</param>
    /// <param name="edits">Die Ersetzungen in beliebiger Reihenfolge.</param>
    /// <returns>Der neue Text, oder derselbe String, wenn es keine Ersetzung gibt.</returns>
    public static string ApplyAll(string text, List<TextEdit> edits)
    {
        if (edits.Count == 0)
        {
            return text;
        }
        // Von hinten nach vorn: Jede Ersetzung verschiebt nur, was hinter ihr liegt.
        edits.Sort((left, right) => right.Start.CompareTo(left.Start));
        StringBuilder builder = new(text);
        foreach (TextEdit edit in edits)
        {
            _ = builder.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        }
        return builder.ToString();
    }
}

/// <summary>Ein Eintrag einer Schlagwort-Liste: seine Lage im Text und sein gelesener Wert.</summary>
/// <param name="Start">Erstes Zeichen des Eintrags im Text, Anführungszeichen eingeschlossen.</param>
/// <param name="End">Erstes Zeichen hinter dem Eintrag.</param>
/// <param name="Value">Der Wert ohne Anführungszeichen.</param>
internal readonly record struct TagsItem(int Start, int End, string Value);

/// <summary>Die Entscheidung für einen Eintrag.</summary>
/// <param name="Action">Was geschieht.</param>
/// <param name="NewName">Der neue Name ohne Raute, nur bei <see cref="ItemAction.Rename"/>.</param>
internal readonly record struct ItemDecision(ItemAction Action, string NewName);

/// <summary>Der Wert unter dem Schlüssel <c>tags</c> in einer der Formen, die der Umschreiber ändern kann.</summary>
internal abstract class TagsNode
{
    /// <summary>Liefert die Ersetzungen für diesen Wert.</summary>
    /// <param name="yaml">Der ganze Frontmatter-Block.</param>
    /// <param name="operations">Die Operationen dieses Aufrufs.</param>
    /// <returns>Die Ersetzungen; leer, wenn keine Operation den Wert betrifft.</returns>
    public abstract IEnumerable<TextEdit> Rewrite(string yaml, TagOperations operations);

    /// <summary>Entscheidet je Eintrag, ob er bleibt, umbenannt wird oder wegfällt.</summary>
    /// <param name="values">Die gelesenen Werte der Einträge, in Reihenfolge.</param>
    /// <param name="operations">Die Operationen dieses Aufrufs.</param>
    /// <param name="decisions">Je Eintrag die Entscheidung.</param>
    /// <returns><see langword="false"/>, wenn keine Operation die Liste betrifft.</returns>
    protected static bool TryDecide(IReadOnlyList<string> values, TagOperations operations, out ItemDecision[] decisions)
    {
        decisions = new ItemDecision[values.Count];
        HashSet<string> seenSlugs = new(StringComparer.Ordinal);
        bool touched = false;
        for (int index = 0; index < values.Count; index++)
        {
            decisions[index] = DecideOne(values[index], operations, seenSlugs);
            touched |= decisions[index].Action != ItemAction.Keep;
        }
        return touched;
    }

    /// <summary>
    /// Setzt den neuen Namen in die Gestalt des alten Eintrags: dieselben Anführungszeichen und
    /// dieselbe führende Raute. Die Umbenennung betrifft den Namen, nicht die Schreibweise.
    /// </summary>
    /// <param name="originalToken">Der Eintrag, wie er im Text steht.</param>
    /// <param name="newName">Der neue Name ohne Raute.</param>
    /// <returns>Der neue Eintrag.</returns>
    protected static string ShapeLike(string originalToken, string newName)
    {
        bool quoted = IsQuoted(originalToken);
        string inner = quoted ? originalToken[1..^1] : originalToken;
        int hashes = inner.Length - inner.TrimStart('#').Length;
        string shaped = new string('#', hashes) + newName;
        return quoted ? string.Concat(originalToken[..1], shaped, originalToken[^1..]) : shaped;
    }

    /// <summary>Ob der Eintrag in einfachen oder doppelten Anführungszeichen steht.</summary>
    /// <param name="token">Der Eintrag, wie er im Text steht.</param>
    /// <returns><see langword="true"/> bei passenden Anführungszeichen an beiden Enden.</returns>
    protected static bool IsQuoted(string token) =>
        token.Length >= 2
        && ((token[0] == '"' && token[^1] == '"') || (token[0] == '\'' && token[^1] == '\''));

    // Ein leerer Eintrag (etwa zwischen zwei Kommas) zählt nicht als Schlagwort und darum auch
    // nicht als Doppelter. Ein Eintrag, den das Zusammenführen auf einen schon gesehenen Slug
    // bringt, fällt weg; sonst stünde dasselbe Schlagwort zweimal in der Liste.
    private static ItemDecision DecideOne(string value, TagOperations operations, HashSet<string> seenSlugs)
    {
        if (value.Length == 0)
        {
            return new ItemDecision(ItemAction.Keep, string.Empty);
        }
        bool affected = operations.TryGet(value, out string? replacement);
        string? effective = affected ? replacement : value;
        if (effective is null || !seenSlugs.Add(operations.SlugOrSelf(effective)))
        {
            return new ItemDecision(ItemAction.Drop, string.Empty);
        }
        return affected ? new ItemDecision(ItemAction.Rename, effective) : new ItemDecision(ItemAction.Keep, string.Empty);
    }
}
