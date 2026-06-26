using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MdExplorer.Parser.Abstractions;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Reine Funktionen zum projektweiten Umschreiben von Hashtags in einem Markdown-Dokument.
/// Verarbeitet sowohl Body-Vorkommen (Regex mit denselben Boundary-Regeln wie der
/// <see cref="TagExtractor"/>) als auch YAML-Frontmatter-Eintraege im <c>tags</c>-Feld
/// (zeilenbasiert, damit andere Frontmatter-Felder bitgenau erhalten bleiben).
/// </summary>
public sealed partial class MarkdownTagRewriter : IMarkdownTagRewriter
{
    [GeneratedRegex(@"(?<![\w#])#(?<name>[A-Za-zÄÖÜäöüß][A-Za-z0-9ÄÖÜäöüß_\-]+)(?![\w-])", RegexOptions.CultureInvariant)]
    private static partial Regex HashtagRegex();

    [GeneratedRegex(@"^(?<indent>\s*)tags\s*:\s*(?<value>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex FrontmatterTagsLineRegex();

    [GeneratedRegex(@"^(?<indent>\s+)-\s+(?<value>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex FrontmatterListItemRegex();

    private readonly ITagNormalizer _normalizer;

    /// <summary>Erzeugt den Rewriter und bindet den Normalisierer fuer Slug-Vergleiche.</summary>
    public MarkdownTagRewriter(ITagNormalizer normalizer)
    {
        ArgumentNullException.ThrowIfNull(normalizer);
        _normalizer = normalizer;
    }

    /// <inheritdoc />
    public string Apply(string original, IReadOnlyDictionary<string, string?> operations)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count == 0 || original.Length == 0)
        {
            return original;
        }

        Dictionary<string, string?> normalizedOps = NormalizeOperationKeys(operations);

        FrontmatterBounds? bounds = TryLocateFrontmatter(original);
        if (bounds is null)
        {
            return RewriteBody(original, normalizedOps);
        }

        string frontmatterSlice = original.Substring(bounds.ContentStart, bounds.ContentLength);
        string rewrittenFrontmatter = RewriteFrontmatterTagsBlock(frontmatterSlice, normalizedOps);

        string before = original[..bounds.ContentStart];
        string after = original[(bounds.ContentStart + bounds.ContentLength)..];
        string body = RewriteBody(after, normalizedOps);

        if (string.Equals(rewrittenFrontmatter, frontmatterSlice, StringComparison.Ordinal)
            && string.Equals(body, after, StringComparison.Ordinal))
        {
            return original;
        }

        return string.Concat(before, rewrittenFrontmatter, body);
    }

    private Dictionary<string, string?> NormalizeOperationKeys(IReadOnlyDictionary<string, string?> operations)
    {
        Dictionary<string, string?> normalized = new(operations.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string?> entry in operations)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Key);
            string slugKey = _normalizer.ToSlug(entry.Key);
            normalized[slugKey] = entry.Value;
        }
        return normalized;
    }

    private string RewriteBody(string text, Dictionary<string, string?> operations)
    {
        if (text.Length == 0)
        {
            return text;
        }
        return HashtagRegex().Replace(text, match =>
        {
            string captured = match.Groups["name"].Value;
            string slug;
            try
            {
                slug = _normalizer.ToSlug(captured);
            }
            catch (ArgumentException)
            {
                return match.Value;
            }
            if (!operations.TryGetValue(slug, out string? replacement))
            {
                return match.Value;
            }
            return replacement is null ? string.Empty : $"#{replacement}";
        });
    }

    private string RewriteFrontmatterTagsBlock(string frontmatter, Dictionary<string, string?> operations)
    {
        string[] sourceLines = frontmatter.Split('\n');
        List<string> output = new(sourceLines.Length);
        bool changed = false;
        int index = 0;
        while (index < sourceLines.Length)
        {
            string lineWithEol = sourceLines[index];
            string line = lineWithEol.TrimEnd('\r');
            // Inline-Form: "tags: foo, bar" oder "tags: [foo, bar]"
            Match inlineMatch = FrontmatterTagsLineRegex().Match(line);
            if (!inlineMatch.Success)
            {
                output.Add(lineWithEol);
                index++;
                continue;
            }
            string value = inlineMatch.Groups["value"].Value.Trim();
            if (value.Length == 0)
            {
                // Block-Form: ggf. folgende "- item"-Zeilen mitnehmen
                output.Add(lineWithEol);
                int processedThrough = RewriteBlockSequenceInto(sourceLines, index + 1, operations, output, out bool blockChanged);
                changed |= blockChanged;
                index = processedThrough + 1;
                continue;
            }
            string newValue = RewriteInlineTagsValue(value, operations);
            if (!string.Equals(newValue, value, StringComparison.Ordinal))
            {
                string carriage = lineWithEol.EndsWith('\r') ? "\r" : string.Empty;
                output.Add($"{inlineMatch.Groups["indent"].Value}tags: {newValue}{carriage}");
                changed = true;
            }
            else
            {
                output.Add(lineWithEol);
            }
            index++;
        }
        return changed ? string.Join('\n', output) : frontmatter;
    }

    private string RewriteInlineTagsValue(string value, Dictionary<string, string?> operations)
    {
        bool bracketed = value.StartsWith('[') && value.EndsWith(']');
        string inner = bracketed ? value[1..^1] : value;
        List<string> rewritten = MapInlineTokens(inner, operations);
        string joined = string.Join(", ", rewritten);
        return bracketed ? $"[{joined}]" : joined;
    }

    private List<string> MapInlineTokens(string inner, Dictionary<string, string?> operations)
    {
        string[] tokens = inner.Split(',', StringSplitOptions.None);
        List<string> rewritten = new(tokens.Length);
        // Dedupe ueber Slug — Merge-Operationen sollen keine Doppel-Eintraege erzeugen.
        HashSet<string> seenSlugs = new(StringComparer.Ordinal);
        foreach (string token in tokens)
        {
            string stripped = token.Trim();
            if (stripped.Length == 0)
            {
                continue;
            }
            string? mapped = MapTagToken(StripYamlQuotes(stripped), operations);
            if (mapped is null)
            {
                continue;
            }
            if (!seenSlugs.Add(ToSlugSafe(mapped)))
            {
                continue;
            }
            rewritten.Add(mapped);
        }
        return rewritten;
    }

    private string ToSlugSafe(string raw)
    {
        try
        {
            return _normalizer.ToSlug(raw);
        }
        catch (ArgumentException)
        {
            return raw;
        }
    }

    private int RewriteBlockSequenceInto(
        string[] sourceLines,
        int startIndex,
        Dictionary<string, string?> operations,
        List<string> output,
        out bool changed)
    {
        changed = false;
        int lastIndexProcessed = startIndex - 1;
        HashSet<string> seenSlugs = new(StringComparer.Ordinal);
        for (int index = startIndex; index < sourceLines.Length; index++)
        {
            string lineWithEol = sourceLines[index];
            Match listMatch = FrontmatterListItemRegex().Match(lineWithEol.TrimEnd('\r'));
            if (!listMatch.Success)
            {
                break;
            }
            HandleBlockSequenceItem(lineWithEol, listMatch, operations, output, seenSlugs, ref changed);
            lastIndexProcessed = index;
        }
        return lastIndexProcessed;
    }

    private void HandleBlockSequenceItem(
        string lineWithEol,
        Match listMatch,
        Dictionary<string, string?> operations,
        List<string> output,
        HashSet<string> seenSlugs,
        ref bool changed)
    {
        string token = StripYamlQuotes(listMatch.Groups["value"].Value.Trim());
        string? mapped = MapTagToken(token, operations);
        if (mapped is null)
        {
            // Loeschen: Zeile NICHT in die Output-Liste uebernehmen.
            changed = true;
            return;
        }
        if (!seenSlugs.Add(ToSlugSafe(mapped)))
        {
            // Dedupe: Merge hat hier denselben Slug bereits gesehen.
            changed = true;
            return;
        }
        if (!string.Equals(mapped, token, StringComparison.Ordinal))
        {
            string carriage = lineWithEol.EndsWith('\r') ? "\r" : string.Empty;
            output.Add($"{listMatch.Groups["indent"].Value}- {mapped}{carriage}");
            changed = true;
            return;
        }
        output.Add(lineWithEol);
    }

    private string? MapTagToken(string token, Dictionary<string, string?> operations)
    {
        string trimmed = token.TrimStart('#');
        if (trimmed.Length == 0)
        {
            return token;
        }
        string slug;
        try
        {
            slug = _normalizer.ToSlug(trimmed);
        }
        catch (ArgumentException)
        {
            return token;
        }
        if (!operations.TryGetValue(slug, out string? replacement))
        {
            return token;
        }
        return replacement;
    }

    private static string StripYamlQuotes(string token)
    {
        if (token.Length >= 2
            && ((token[0] == '"' && token[^1] == '"')
                || (token[0] == '\'' && token[^1] == '\'')))
        {
            return token[1..^1];
        }
        return token;
    }

    private static FrontmatterBounds? TryLocateFrontmatter(string text)
    {
        if (!text.StartsWith("---", StringComparison.Ordinal))
        {
            return null;
        }
        int firstNewline = text.IndexOf('\n', 3);
        if (firstNewline < 0)
        {
            return null;
        }
        string firstLine = text[..firstNewline].TrimEnd('\r').Trim();
        if (!string.Equals(firstLine, "---", StringComparison.Ordinal))
        {
            return null;
        }
        int contentStart = firstNewline + 1;
        int cursor = contentStart;
        while (cursor < text.Length)
        {
            int eol = text.IndexOf('\n', cursor);
            if (eol < 0)
            {
                return null;
            }
            string current = text.Substring(cursor, eol - cursor).TrimEnd('\r');
            if (string.Equals(current, "---", StringComparison.Ordinal)
                || string.Equals(current, "...", StringComparison.Ordinal))
            {
                int contentLength = cursor - contentStart;
                return new FrontmatterBounds(contentStart, contentLength);
            }
            cursor = eol + 1;
        }
        return null;
    }

    private sealed record FrontmatterBounds(int ContentStart, int ContentLength)
    {
        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"[{ContentStart}, {ContentLength}]");
    }
}
