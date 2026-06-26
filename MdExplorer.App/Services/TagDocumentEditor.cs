using System.Text.RegularExpressions;

namespace MdExplorer.App.Services;

/// <summary>
/// Reine Funktionen zur CRUD-Pflege von Hashtag-Tokens (<c>#tag</c>) in einem
/// Markdown-Dokument. Insertion-Strategie: ein verwalteter HTML-Kommentar-Block
/// <c>&lt;!-- mdexplorer-tags: #a #b --&gt;</c> am Dateiende. Roh-Text bleibt
/// ansonsten unangetastet — Roundtrip-fähig.
/// </summary>
internal static partial class TagDocumentEditor
{
    /// <summary>Prefix-Marker des verwalteten Tag-Blocks.</summary>
    public const string ManagedBlockPrefix = "<!-- mdexplorer-tags:";

    /// <summary>Suffix-Marker des verwalteten Tag-Blocks.</summary>
    public const string ManagedBlockSuffix = "-->";

    [GeneratedRegex(
        @"<!-- mdexplorer-tags:(?<body>[^>]*)-->",
        RegexOptions.CultureInvariant)]
    private static partial Regex ManagedBlockRegex();

    /// <summary>
    /// Fuegt <paramref name="tagName"/> dem Dokument hinzu, sofern noch nicht vorhanden.
    /// Liefert den unveraenderten Text, wenn der Tag bereits irgendwo im Body steht
    /// (Vermeidung von Duplikaten im verwalteten Block).
    /// </summary>
    public static string Add(string text, string tagName, IReadOnlyList<string> currentTags)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
        ArgumentNullException.ThrowIfNull(currentTags);

        string sanitized = tagName.TrimStart('#').Trim();
        if (sanitized.Length == 0)
        {
            return text;
        }
        if (ContainsTagOrdinalIgnoreCase(currentTags, sanitized))
        {
            return text;
        }

        Match block = ManagedBlockRegex().Match(text);
        if (block.Success)
        {
            string body = block.Groups["body"].Value.TrimEnd();
            string newBody = $"{body} #{sanitized} ";
            string replacement = $"{ManagedBlockPrefix}{newBody}{ManagedBlockSuffix}";
            return string.Concat(text.AsSpan(0, block.Index), replacement, text.AsSpan(block.Index + block.Length));
        }

        string separator = text.Length == 0 || text.EndsWith('\n') ? string.Empty : "\n\n";
        return $"{text}{separator}{ManagedBlockPrefix} #{sanitized} {ManagedBlockSuffix}\n";
    }

    /// <summary>
    /// Entfernt alle <c>#tagName</c>-Vorkommen aus dem Text. Wird der verwaltete Block
    /// dadurch leer, wird er ebenfalls entfernt.
    /// </summary>
    public static string Remove(string text, string tagName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);

        string sanitized = tagName.TrimStart('#').Trim();
        if (sanitized.Length == 0)
        {
            return text;
        }

        string stripped = BuildHashtagRegex(sanitized).Replace(text, string.Empty);
        return CleanupEmptyManagedBlock(stripped);
    }

    /// <summary>
    /// Ersetzt alle <c>#oldName</c>-Vorkommen durch <c>#newName</c>.
    /// </summary>
    public static string Rename(string text, string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        string sanitizedOld = oldName.TrimStart('#').Trim();
        string sanitizedNew = newName.TrimStart('#').Trim();
        if (sanitizedOld.Length == 0 || sanitizedNew.Length == 0)
        {
            return text;
        }
        if (string.Equals(sanitizedOld, sanitizedNew, StringComparison.Ordinal))
        {
            return text;
        }

        return BuildHashtagRegex(sanitizedOld).Replace(text, $"#{sanitizedNew}");
    }

    private static Regex BuildHashtagRegex(string tagName)
    {
        // Identische Boundary-Regeln wie TagExtractor: davor kein Wortzeichen / kein '#',
        // danach kein Wortzeichen / kein '-' (sonst wuerden Praefix-Treffer falsch matchen).
        string pattern = $@"(?<![\w#])#{Regex.Escape(tagName)}(?![\w\-])";
        return new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static string CleanupEmptyManagedBlock(string text)
    {
        Match block = ManagedBlockRegex().Match(text);
        if (!block.Success)
        {
            return text;
        }
        string body = block.Groups["body"].Value.Trim();
        if (body.Length > 0)
        {
            return text;
        }
        int start = block.Index;
        int end = block.Index + block.Length;
        // Eine eventuelle Folge-Leerzeile mit entfernen.
        while (end < text.Length && (text[end] == '\r' || text[end] == '\n'))
        {
            end++;
        }
        return string.Concat(text.AsSpan(0, start), text.AsSpan(end));
    }

    private static bool ContainsTagOrdinalIgnoreCase(IReadOnlyList<string> tags, string candidate)
    {
        for (int index = 0; index < tags.Count; index++)
        {
            if (string.Equals(tags[index], candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
