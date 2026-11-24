using System.Text;

namespace MdExplorer.Parser.Services;

/// <summary>Ein einzelner Text mit Kommas: <c>tags: a, b</c> oder <c>tags: "a, b"</c>.</summary>
/// <param name="start">Erstes Zeichen des Werts, Anführungszeichen eingeschlossen.</param>
/// <param name="end">Erstes Zeichen hinter dem Wert.</param>
internal sealed class CommaListNode(int start, int end) : TagsNode
{
    /// <inheritdoc />
    /// <remarks>
    /// Der Index teilt diesen Text an den Kommas. Die Teile behalten ihre Abstände; fällt der erste
    /// oder letzte weg, bekommt der neue Rand den Abstand des alten.
    /// </remarks>
    public override IEnumerable<TextEdit> Rewrite(string yaml, TagOperations operations)
    {
        string raw = yaml[start..end];
        bool quoted = IsQuoted(raw);
        string inner = quoted ? raw[1..^1] : raw;
        string[] segments = inner.Split(',');
        if (!TryDecide([.. segments.Select(segment => segment.Trim())], operations, out ItemDecision[] decisions))
        {
            yield break;
        }
        List<string> kept = [];
        for (int index = 0; index < segments.Length; index++)
        {
            ItemDecision decision = decisions[index];
            if (decision.Action != ItemAction.Drop && segments[index].Trim().Length > 0)
            {
                kept.Add(decision.Action == ItemAction.Keep ? segments[index] : ReplaceToken(segments[index], decision.NewName));
            }
        }
        string joined = string.Join(',', AlignOuterSpacing(kept, segments));
        yield return new TextEdit(start, end - start, quoted ? string.Concat(raw[..1], joined, raw[^1..]) : joined);
    }

    private static string ReplaceToken(string segment, string newName)
    {
        string token = segment.Trim();
        int tokenStart = segment.IndexOf(token, StringComparison.Ordinal);
        return string.Concat(segment.AsSpan(0, tokenStart), ShapeLike(token, newName), segment.AsSpan(tokenStart + token.Length));
    }

    private static List<string> AlignOuterSpacing(List<string> kept, string[] original)
    {
        if (kept.Count == 0)
        {
            return kept;
        }
        string leading = original[0][..^original[0].TrimStart().Length];
        string trailing = original[^1][original[^1].TrimEnd().Length..];
        kept[0] = leading + kept[0].TrimStart();
        kept[^1] = kept[^1].TrimEnd() + trailing;
        return kept;
    }
}
