using System.Text;
using Markdig.Extensions.Yaml;
using Markdig.Helpers;
using Markdig.Syntax;
using MdExplorer.Parser.Abstractions;
using YamlDotNet.RepresentationModel;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Liest den YAML-Frontmatter-Block (zwischen <c>---</c>-Zeilen am Dokument-Anfang) und liefert eine
/// flache Schlüssel/Wert-Repräsentation. Listen werden kommagetrennt zusammengeführt.
/// </summary>
public sealed class FrontmatterExtractor : IFrontmatterExtractor
{
    private static readonly IReadOnlyDictionary<string, string> EmptyDictionary =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Extract(MarkdownDocument ast)
    {
        ArgumentNullException.ThrowIfNull(ast);

        YamlFrontMatterBlock? block = ast.Descendants<YamlFrontMatterBlock>().FirstOrDefault();
        if (block is null)
        {
            return EmptyDictionary;
        }

        string yamlBody = ExtractYamlBody(block);
        if (yamlBody.Length == 0)
        {
            return EmptyDictionary;
        }

        YamlStream stream = new();
        using StringReader reader = new(yamlBody);
        try
        {
            stream.Load(reader);
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return EmptyDictionary;
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
        {
            return EmptyDictionary;
        }

        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<YamlNode, YamlNode> entry in mapping.Children)
        {
            if (entry.Key is not YamlScalarNode keyScalar || keyScalar.Value is null)
            {
                continue;
            }
            string key = keyScalar.Value;
            string? value = RenderValue(entry.Value);
            if (value is null)
            {
                continue;
            }
            result[key] = value;
        }
        return result;
    }

    private static string ExtractYamlBody(YamlFrontMatterBlock block)
    {
        StringBuilder builder = new();
        StringLineGroup lines = block.Lines;
        for (int index = 0; index < lines.Count; index++)
        {
            string lineText = lines.Lines[index].Slice.ToString();
            if (string.Equals(lineText, "---", StringComparison.Ordinal) || string.Equals(lineText, "...", StringComparison.Ordinal))
            {
                continue;
            }
            _ = builder.Append(lineText);
            _ = builder.Append('\n');
        }
        return builder.ToString();
    }

    private static string? RenderValue(YamlNode node)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
                return scalar.Value ?? string.Empty;
            case YamlSequenceNode sequence:
                List<string> parts = new(sequence.Children.Count);
                foreach (YamlNode child in sequence.Children)
                {
                    string? rendered = RenderValue(child);
                    if (rendered is not null)
                    {
                        parts.Add(rendered);
                    }
                }
                return string.Join(", ", parts);
            case YamlMappingNode:
                return null;
            default:
                return null;
        }
    }
}
