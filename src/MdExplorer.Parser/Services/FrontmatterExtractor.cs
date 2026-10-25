using System.Text;
using Markdig.Extensions.Yaml;
using Markdig.Helpers;
using Markdig.Syntax;
using MdExplorer.Parser.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Liest den YAML-Frontmatter-Block (zwischen <c>---</c>-Zeilen am Dokument-Anfang) und liefert eine
/// flache Schlüssel/Wert-Repräsentation. Listen werden kommagetrennt zusammengeführt.
/// </summary>
/// <remarks>
/// Bevor das YAML geladen wird, prüft <see cref="FrontmatterLimits"/> Größe, Aliase und Tiefe.
/// Verletzt der Block eine Grenze, gilt das Frontmatter als kaputt, wie bei einem Syntaxfehler,
/// und das Dokument wird ohne Frontmatter indiziert.
/// </remarks>
public sealed partial class FrontmatterExtractor : IFrontmatterExtractor
{
    /// <summary>Größte Länge des Frontmatter-Blocks in Zeichen; Begründung an <see cref="FrontmatterLimits.MaxLength"/>.</summary>
    public const int MaxFrontmatterLength = FrontmatterLimits.MaxLength;

    /// <summary>Größte Verschachtelungstiefe; Begründung an <see cref="FrontmatterLimits.MaxNestingDepth"/>.</summary>
    public const int MaxNestingDepth = FrontmatterLimits.MaxNestingDepth;

    private static readonly IReadOnlyDictionary<string, string> EmptyDictionary =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<FrontmatterExtractor> _logger;

    /// <summary>Erzeugt den Extractor ohne Protokoll.</summary>
    public FrontmatterExtractor()
        : this(NullLogger<FrontmatterExtractor>.Instance)
    {
    }

    /// <summary>Erzeugt den Extractor mit Protokoll für verworfene Frontmatter-Blöcke.</summary>
    /// <param name="logger">Protokoll, in dem ein verworfener Block mit Grund erscheint.</param>
    public FrontmatterExtractor(ILogger<FrontmatterExtractor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Extract(MarkdownDocument ast)
    {
        ArgumentNullException.ThrowIfNull(ast);

        string yamlBody = ExtractYamlBody(ast);
        if (yamlBody.Length == 0)
        {
            return EmptyDictionary;
        }

        return LoadMapping(yamlBody) is { } mapping ? ToDictionary(mapping) : EmptyDictionary;
    }

    private static Dictionary<string, string> ToDictionary(YamlMappingNode mapping)
    {
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

    // Markdig erkennt Frontmatter nur am Anfang des Dokuments; ein Gang durch den ganzen Baum
    // fände nichts anderes und kostete bei jeder Datei ohne Frontmatter den vollen Durchlauf.
    private static string ExtractYamlBody(MarkdownDocument ast)
    {
        if (ast.Count == 0 || ast[0] is not YamlFrontMatterBlock block)
        {
            return string.Empty;
        }
        StringBuilder builder = new();
        StringLineGroup lines = block.Lines;
        for (int index = 0; index < lines.Count; index++)
        {
            string lineText = lines.Lines[index].Slice.ToString();

            // Markdig liefert die Trennzeilen als Teil des Blocks mit; YAML soll sie nicht sehen.
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
            // Ein verschachtelter Abschnitt lässt sich nicht als eine Textzeile darstellen und fällt
            // weg, ebenso jede andere Knotenart.
            default:
                return null;
        }
    }

    // Der Pfad der Datei ist hier nicht bekannt; der Parser bekommt nur den Text. Die Länge des
    // Blocks hilft beim Wiederfinden, und der Grund sagt, was am Frontmatter zu ändern ist.
    [LoggerMessage(EventId = 212, Level = LogLevel.Warning, Message = "Frontmatter verworfen ({Rejection}, {Length} Zeichen); das Dokument wird ohne Frontmatter indiziert.")]
    private static partial void LogFrontmatterRejected(ILogger logger, FrontmatterRejection rejection, int length);

    // Liefert die oberste Zuordnung oder null, wenn der Block zu groß, zu tief, kaputt oder keine
    // Zuordnung ist. Kaputtes YAML kostet nur das Frontmatter; das Dokument wird trotzdem indiziert.
    private YamlMappingNode? LoadMapping(string yamlBody)
    {
        YamlStream stream = new();
        try
        {
            FrontmatterRejection rejection = FrontmatterLimits.FindRejection(yamlBody);
            if (rejection != FrontmatterRejection.None)
            {
                LogFrontmatterRejected(_logger, rejection, yamlBody.Length);
                return null;
            }

            using StringReader reader = new(yamlBody);
            stream.Load(reader);
        }
        catch (YamlException)
        {
            return null;
        }
        return stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
    }
}
