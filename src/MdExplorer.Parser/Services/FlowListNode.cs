using System.Text;

namespace MdExplorer.Parser.Services;

/// <summary>Eine Liste in eckigen Klammern: <c>tags: [a, b] # Kommentar</c>.</summary>
/// <param name="start">Lage der öffnenden Klammer.</param>
/// <param name="end">Erstes Zeichen hinter der schließenden Klammer.</param>
/// <param name="items">Die Einträge.</param>
internal sealed class FlowListNode(int start, int end, List<TagsItem> items) : TagsNode
{
    /// <inheritdoc />
    /// <remarks>
    /// Neu gebaut wird nur der Bereich zwischen den Klammern. Ein bleibender Eintrag behält das
    /// Trennzeichen, das im Original vor ihm stand; fällt der erste weg, rückt der nächste ohne
    /// sein Komma an den Rand. Was vor der ersten und hinter der letzten Klammer steht, auch ein
    /// Kommentar, bleibt Zeichen für Zeichen.
    /// </remarks>
    public override IEnumerable<TextEdit> Rewrite(string yaml, TagOperations operations)
    {
        if (!TryDecide([.. items.Select(item => item.Value)], operations, out ItemDecision[] decisions))
        {
            yield break;
        }
        StringBuilder builder = new(yaml[start..items[0].Start]);
        bool first = true;
        for (int index = 0; index < items.Count; index++)
        {
            ItemDecision decision = decisions[index];
            if (decision.Action == ItemAction.Drop)
            {
                continue;
            }
            if (!first)
            {
                _ = builder.Append(yaml, items[index - 1].End, items[index].Start - items[index - 1].End);
            }
            string token = yaml[items[index].Start..items[index].End];
            _ = builder.Append(decision.Action == ItemAction.Keep ? token : ShapeLike(token, decision.NewName));
            first = false;
        }
        _ = builder.Append(yaml, items[^1].End, end - items[^1].End);
        yield return new TextEdit(start, end - start, builder.ToString());
    }
}
