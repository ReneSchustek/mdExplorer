using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdExplorer.Core.Abstractions;
using MdExplorer.Parser.Abstractions;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Schreibt Schlagworte projektweit um: Hashtags im Rumpf an genau den Stellen, an denen der
/// <see cref="TagExtractor"/> zählt, und die Einträge unter <c>tags</c> im Frontmatter über
/// <see cref="FrontmatterTagsRewriter"/>. Alle übrigen Zeichen der Datei bleiben, wie sie sind.
/// </summary>
public sealed partial class MarkdownTagRewriter : IMarkdownTagRewriter
{
    [GeneratedRegex(HashtagPattern.Expression, RegexOptions.CultureInvariant, HashtagPattern.MatchTimeoutMilliseconds)]
    private static partial Regex HashtagRegex();

    private readonly ITagNormalizer _normalizer;
    private readonly ISettingsService? _settingsService;

    // Die Pipeline des Index, ergänzt um genaue Quellpositionen: Ersetzt wird im Originaltext,
    // und dafür braucht jedes Literal seine Lage dort, nicht nur seinen Inhalt.
    private readonly MarkdownPipeline _pipeline;

    /// <summary>Erzeugt den Umschreiber; Hashtags im Rumpf werden immer umgeschrieben.</summary>
    /// <param name="normalizer">Bildet die Slugs für den Vergleich.</param>
    public MarkdownTagRewriter(ITagNormalizer normalizer)
    {
        ArgumentNullException.ThrowIfNull(normalizer);
        _normalizer = normalizer;
        _pipeline = IndexPipeline.CreateBuilder(normalizer.TryToSlug).UsePreciseSourceLocation().Build();
    }

    /// <summary>Erzeugt den Umschreiber, der Hashtags im Rumpf nur anfasst, wenn der Index sie auch zählt.</summary>
    /// <param name="normalizer">Bildet die Slugs für den Vergleich.</param>
    /// <param name="settingsService">Liefert den Schalter „Hashtags im Text auswerten“.</param>
    public MarkdownTagRewriter(ITagNormalizer normalizer, ISettingsService settingsService)
        : this(normalizer)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        _settingsService = settingsService;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Ein neuer Name ist kein gültiger Schlagwort-Name.</exception>
    public string Apply(string original, IReadOnlyDictionary<string, string?> operations)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(operations);

        // Zuerst prüfen: Ein ungültiger neuer Name fällt beim ersten Aufruf auf, noch bevor
        // irgendeine Datei umgeschrieben ist.
        TagOperations tagOperations = new(_normalizer, operations);
        if (tagOperations.IsEmpty || original.Length == 0)
        {
            return original;
        }

        FrontmatterBounds? bounds = FrontmatterBounds.Locate(original);
        int bodyStart = bounds?.End ?? 0;

        // Der Rumpf wird zuerst umgeschrieben und nur ab bodyStart. Alles davor, also auch der
        // Frontmatter-Block, behält dabei seine Lage, und die Grenzen bleiben gültig.
        string withBody = CountsBodyHashtags() ? RewriteBody(original, bodyStart, tagOperations) : original;
        return bounds is null ? withBody : RewriteFrontmatter(withBody, bounds, tagOperations);
    }

    private static string RewriteFrontmatter(string text, FrontmatterBounds bounds, TagOperations tagOperations)
    {
        string frontmatter = text.Substring(bounds.ContentStart, bounds.ContentLength);
        string rewritten = FrontmatterTagsRewriter.Rewrite(frontmatter, tagOperations);
        return ReferenceEquals(rewritten, frontmatter)
            ? text
            : string.Concat(text.AsSpan(0, bounds.ContentStart), rewritten, text.AsSpan(bounds.End));
    }

    private static IEnumerable<SourceSpan> RewritableSpans(MarkdownDocument ast, string text, int bodyStart)
    {
        foreach (LiteralInline literal in TagExtractor.EnumerateBodyLiterals(ast))
        {
            SourceSpan span = literal.Span;
            if (span.IsEmpty || span.Start < bodyStart || span.End >= text.Length)
            {
                continue;
            }
            if (string.Equals(text.Substring(span.Start, span.Length), literal.Content.ToString(), StringComparison.Ordinal))
            {
                yield return span;
            }
        }
    }

    // Ein Farbwert wie #cafe erfüllt jede Bedingung eines Schlagworts und wird vom Index trotzdem
    // nicht gezählt; umbenannt würde aus der Farbe ein Wort.
    private static string RewriteHashtags(string text, TagOperations operations)
    {
        return HashtagRegex().Replace(text, match =>
        {
            string captured = match.Groups["name"].Value;
            if (HashtagPattern.IsColorLiteral(captured) || !operations.TryGet(captured, out string? replacement))
            {
                return match.Value;
            }
            return replacement is null ? string.Empty : "#" + replacement;
        });
    }

    /// <summary>
    /// Ersetzt Hashtags in den Textstücken, die der Index als Schlagwort zählt.
    /// </summary>
    /// <remarks>
    /// Das Dokument wird mit der Pipeline des Index gelesen, und ersetzt wird nur innerhalb der
    /// Literale, die <see cref="TagExtractor.EnumerateBodyLiterals"/> liefert. Eine Raute in
    /// Code, Codeblock, Frontmatter oder Verweisziel bleibt damit stehen, genau wie sie beim
    /// Zählen fehlt. Weicht die Quelle eines Literals von seinem gelesenen Inhalt ab
    /// (Escape-Zeichen, Entitäten), bleibt es unangetastet: Dort ließe sich nicht sicher sagen,
    /// welche Zeichen in der Datei zu ersetzen wären.
    /// </remarks>
    private string RewriteBody(string text, int bodyStart, TagOperations operations)
    {
        if (bodyStart >= text.Length)
        {
            return text;
        }

        MarkdownDocument ast = Markdown.Parse(text, _pipeline);
        StringBuilder? output = null;
        int copiedUpTo = 0;
        foreach (SourceSpan span in RewritableSpans(ast, text, bodyStart))
        {
            string source = text.Substring(span.Start, span.Length);
            string rewritten = RewriteHashtags(source, operations);
            if (string.Equals(rewritten, source, StringComparison.Ordinal))
            {
                continue;
            }
            output ??= new StringBuilder(text.Length);
            _ = output.Append(text, copiedUpTo, span.Start - copiedUpTo).Append(rewritten);
            copiedUpTo = span.End + 1;
        }

        return output is null ? text : output.Append(text, copiedUpTo, text.Length - copiedUpTo).ToString();
    }

    // Steht die Auswertung von Hashtags im Text aus, zählt der Index eine Raute im Rumpf nicht als
    // Schlagwort. Die Verwaltung änderte dann Text, der mit dem Schlagwort nichts zu tun hat.
    private bool CountsBodyHashtags() => _settingsService?.Current.Indexing.AutoExtractHashtags ?? true;

    /// <summary>
    /// Lage des Frontmatter-Inhalts im Text, ohne die beiden Trennzeilen. Davor und danach bleibt alles
    /// unangetastet, nur der Rumpf läuft noch durch die Hashtag-Ersetzung.
    /// </summary>
    /// <param name="ContentStart">Erstes Zeichen nach der öffnenden Trennzeile.</param>
    /// <param name="ContentLength">Länge bis vor die schließende Trennzeile.</param>
    private sealed record FrontmatterBounds(int ContentStart, int ContentLength)
    {
        /// <summary>Erstes Zeichen hinter dem Inhalt, also die schließende Trennzeile.</summary>
        public int End => ContentStart + ContentLength;

        /// <summary>Findet den Block, wenn der Text mit einer Trennzeile <c>---</c> beginnt.</summary>
        public static FrontmatterBounds? Locate(string text)
        {
            if (!text.StartsWith("---", StringComparison.Ordinal))
            {
                return null;
            }
            int firstNewline = text.IndexOf('\n', 3);
            if (firstNewline < 0 || !string.Equals(text[..firstNewline].TrimEnd('\r').Trim(), "---", StringComparison.Ordinal))
            {
                return null;
            }
            int contentStart = firstNewline + 1;
            for (int cursor = contentStart; cursor < text.Length;)
            {
                int eol = text.IndexOf('\n', cursor);
                if (eol < 0)
                {
                    return null;
                }
                string line = text[cursor..eol].TrimEnd('\r');
                if (line is "---" or "...")
                {
                    return new FrontmatterBounds(contentStart, cursor - contentStart);
                }
                cursor = eol + 1;
            }
            return null;
        }
    }
}
