using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Schreibt die Schlagworte im Frontmatter um und lässt jedes andere Zeichen des Blocks stehen.
/// </summary>
/// <remarks>
/// <para>
/// Wo die Schlagworte stehen, sagt der YAML-Leser, nicht ein Zeilenmuster. Er liefert zu jedem
/// Knoten die Lage im Text; geändert werden nur die Einträge unter dem Schlüssel <c>tags</c> auf
/// oberster Ebene, in beliebiger Groß- und Kleinschreibung, so wie der Index sie liest. Ein
/// eingerücktes <c>tags:</c> in einem anderen Abschnitt oder Text in einem Textblock bleibt
/// stehen, Kommentare und Abstände ebenso.
/// </para>
/// <para>
/// Ein Block, den der Index verwirft (zu groß, Alias, zu tief, kaputtes YAML), bleibt unberührt:
/// Was der Index nicht liest, kann auch kein Schlagwort sein.
/// </para>
/// </remarks>
internal static class FrontmatterTagsRewriter
{
    private const string TagsKey = "tags";

    /// <summary>Wendet die Operationen auf die Schlagworte des Blocks an.</summary>
    /// <param name="frontmatter">Der Inhalt zwischen den Trennzeilen, samt Zeilenenden.</param>
    /// <param name="operations">Die Operationen dieses Aufrufs.</param>
    /// <returns>Der umgeschriebene Block, oder derselbe String, wenn nichts zu ändern war.</returns>
    public static string Rewrite(string frontmatter, TagOperations operations)
    {
        List<TagsNode> nodes;
        try
        {
            if (FrontmatterLimits.FindRejection(frontmatter) != FrontmatterRejection.None)
            {
                return frontmatter;
            }
            nodes = FindTopLevelTagNodes(frontmatter);
        }
        catch (YamlException)
        {
            return frontmatter;
        }

        List<TextEdit> edits = [];
        foreach (TagsNode node in nodes)
        {
            edits.AddRange(node.Rewrite(frontmatter, operations));
        }
        return TextEdit.ApplyAll(frontmatter, edits);
    }

    private static List<TagsNode> FindTopLevelTagNodes(string yaml)
    {
        List<TagsNode> found = [];
        using StringReader reader = new(yaml);
        YamlDotNet.Core.Parser parser = new(reader);
        _ = parser.Consume<StreamStart>();
        if (!parser.TryConsume<DocumentStart>(out _) || !parser.TryConsume<MappingStart>(out _))
        {
            return found;
        }

        while (!parser.TryConsume<MappingEnd>(out _))
        {
            if (ReadEntry(parser) is { } node)
            {
                found.Add(node);
            }
        }
        return found;
    }

    // Liest einen Eintrag der obersten Zuordnung; nur unter "tags" kommt ein Knoten zurück.
    private static TagsNode? ReadEntry(IParser parser)
    {
        bool isTags = parser.TryConsume<Scalar>(out Scalar? key)
            && string.Equals(key.Value, TagsKey, StringComparison.OrdinalIgnoreCase);
        if (key is null)
        {
            // Ein zusammengesetzter Schlüssel: Schlüssel und Wert überspringen.
            parser.SkipThisAndNestedEvents();
        }
        if (isTags)
        {
            return ReadTagsNode(parser);
        }
        parser.SkipThisAndNestedEvents();
        return null;
    }

    // Liest den Wert unter "tags". Erkannt werden eine Liste aus einfachen Einträgen (in Klammern
    // oder als Block) und ein einzelner Text mit Kommas. Alles andere, etwa ein Textblock mit "|"
    // oder eine Liste mit Unterlisten, liest auch der Index nicht als Schlagworte und bleibt stehen.
    private static TagsNode? ReadTagsNode(IParser parser)
    {
        if (parser.TryConsume<Scalar>(out Scalar? scalar))
        {
            return scalar.Style is ScalarStyle.Plain or ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted
                ? new CommaListNode(Index(scalar.Start), Index(scalar.End))
                : null;
        }
        if (parser.TryConsume<SequenceStart>(out SequenceStart? sequence))
        {
            return ReadSequence(parser, sequence);
        }
        parser.SkipThisAndNestedEvents();
        return null;
    }

    private static TagsNode? ReadSequence(IParser parser, SequenceStart sequence)
    {
        List<TagsItem> items = [];
        bool onlyScalars = true;
        while (!parser.Accept<SequenceEnd>(out _))
        {
            if (parser.TryConsume<Scalar>(out Scalar? item))
            {
                items.Add(new TagsItem(Index(item.Start), Index(item.End), item.Value));
                continue;
            }
            onlyScalars = false;
            parser.SkipThisAndNestedEvents();
        }
        SequenceEnd end = parser.Consume<SequenceEnd>();
        if (!onlyScalars || items.Count == 0)
        {
            return null;
        }
        return sequence.Style == SequenceStyle.Flow
            ? new FlowListNode(Index(sequence.Start), Index(end.End), items)
            : new BlockListNode(items);
    }

    private static int Index(Mark mark) => checked((int)mark.Index);
}
