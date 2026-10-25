using System.Text;
using System.Text.Json;

namespace MdExplorer.Search.Services;

/// <summary>
/// Bereitet die Spalten eines Volltext-Eintrags auf: Text ohne Frontmatter, Frontmatter als
/// Wortfolge, Schlagworte als Leerzeichen-Liste.
/// </summary>
internal static class SearchIndexText
{
    /// <summary>
    /// Schneidet den Frontmatter-Block ab, damit YAML nicht als Fließtext im Index landet. Seine Werte gehen
    /// getrennt über <see cref="FlattenFrontmatter"/> in die Frontmatter-Spalte.
    /// </summary>
    /// <param name="source">Der Dateiinhalt.</param>
    /// <returns>Der Text nach dem Frontmatter.</returns>
    public static string StripFrontmatter(string source)
    {
        if (!source.StartsWith("---", StringComparison.Ordinal))
        {
            return source;
        }
        int firstLineBreak = source.IndexOf('\n', 3);
        if (firstLineBreak < 0)
        {
            return source;
        }
        int closingIndex = source.IndexOf("\n---", firstLineBreak, StringComparison.Ordinal);
        if (closingIndex < 0)
        {
            return source;
        }
        int afterClosingMarker = closingIndex + 4;
        if (afterClosingMarker >= source.Length)
        {
            return string.Empty;
        }
        int newlineAfter = source.IndexOf('\n', afterClosingMarker);
        return newlineAfter < 0 ? string.Empty : source[(newlineAfter + 1)..];
    }

    /// <summary>
    /// Macht aus dem gespeicherten Frontmatter-JSON eine Wortfolge aus Schlüsseln und Werten, damit die
    /// Volltextsuche auch Feldnamen findet. Kaputtes JSON ergibt eine leere Spalte statt eines Fehlers.
    /// </summary>
    /// <param name="frontmatterJson">Das JSON aus dem Parser.</param>
    /// <returns>Die Wortfolge.</returns>
    public static string FlattenFrontmatter(string frontmatterJson)
    {
        if (string.IsNullOrWhiteSpace(frontmatterJson))
        {
            return string.Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(frontmatterJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? string.Join(' ', document.RootElement.EnumerateObject().Select(WordsOf))
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>Die Schlagwort-Namen einer Datei, durch Leerzeichen getrennt.</summary>
    /// <param name="tagsByFileId">Schlagworte je Datei.</param>
    /// <param name="markdownFileId">Die Datei.</param>
    /// <returns>Die Namen, oder leer.</returns>
    public static string JoinTags(IReadOnlyDictionary<Guid, IReadOnlyList<string>> tagsByFileId, Guid markdownFileId) =>
        tagsByFileId.TryGetValue(markdownFileId, out IReadOnlyList<string>? tagNames)
            ? string.Join(' ', tagNames)
            : string.Empty;

    private static string WordsOf(JsonProperty property)
    {
        StringBuilder builder = new(property.Name);
        _ = builder.Append(' ');
        _ = builder.Append(property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString());
        return builder.ToString();
    }
}
