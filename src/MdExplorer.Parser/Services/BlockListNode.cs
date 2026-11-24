using System.Text;

namespace MdExplorer.Parser.Services;

/// <summary>Eine Liste aus Zeilen mit Spiegelstrich, eingerückt oder nicht.</summary>
/// <param name="items">Die Einträge.</param>
internal sealed class BlockListNode(List<TagsItem> items) : TagsNode
{
    /// <inheritdoc />
    /// <remarks>Ein umbenannter Eintrag wird an seiner Stelle ersetzt, ein wegfallender nimmt seine ganze Zeile mit.</remarks>
    public override IEnumerable<TextEdit> Rewrite(string yaml, TagOperations operations)
    {
        if (!TryDecide([.. items.Select(item => item.Value)], operations, out ItemDecision[] decisions))
        {
            yield break;
        }
        for (int index = 0; index < items.Count; index++)
        {
            TagsItem item = items[index];
            ItemDecision decision = decisions[index];
            if (decision.Action == ItemAction.Drop)
            {
                int lineStart = yaml.LastIndexOf('\n', item.Start) + 1;
                int newline = yaml.IndexOf('\n', item.End);
                int lineEnd = newline < 0 ? yaml.Length : newline + 1;
                yield return new TextEdit(lineStart, lineEnd - lineStart, string.Empty);
            }
            else if (decision.Action == ItemAction.Rename)
            {
                yield return new TextEdit(item.Start, item.End - item.Start, ShapeLike(yaml[item.Start..item.End], decision.NewName));
            }
        }
    }
}
