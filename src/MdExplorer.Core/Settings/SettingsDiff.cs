using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace MdExplorer.Core.Settings;

/// <summary>
/// Strukturierter JSON-Diff zwischen zwei <see cref="MdExplorer.Core.Models.AppSettings"/>-
/// Snapshots. Liefert je geänderter Property einen <see cref="SettingsChangeEntry"/>; eine
/// geänderte Liste erscheint als Änderung je Index.
/// Rein funktional: kein State, keine Seiteneffekte.
/// </summary>
public static class SettingsDiff
{
    // Die Werte landen im Audit-Protokoll, das ein Mensch liest: Umlaute in Pfaden bleiben
    // lesbar, <, > und & werden trotzdem maskiert.
    private static readonly JsonSerializerOptions LiteralOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>
    /// Berechnet den Diff zwischen den beiden serialisierten Snapshots.
    /// </summary>
    /// <param name="previousJson">Vollständiger JSON-Wurzelknoten des vorigen Stands.</param>
    /// <param name="currentJson">Vollständiger JSON-Wurzelknoten des neuen Stands.</param>
    /// <returns>
    /// Liste der Änderungen in deterministischer Reihenfolge (Objekte alphabetisch,
    /// Arrays nach Index). Leere Liste, wenn beide Snapshots identisch sind.
    /// </returns>
    public static IReadOnlyList<SettingsChangeEntry> Compute(string previousJson, string currentJson)
    {
        ArgumentException.ThrowIfNullOrEmpty(previousJson);
        ArgumentException.ThrowIfNullOrEmpty(currentJson);

        JsonNode? previous = JsonNode.Parse(previousJson);
        JsonNode? current = JsonNode.Parse(currentJson);
        List<SettingsChangeEntry> changes = [];
        Walk(string.Empty, previous, current, changes);
        return changes;
    }

    private static void Walk(string path, JsonNode? previous, JsonNode? current, List<SettingsChangeEntry> changes)
    {
        if (previous is JsonObject previousObject && current is JsonObject currentObject)
        {
            WalkObject(path, previousObject, currentObject, changes);
            return;
        }

        if (previous is JsonArray prevArray && current is JsonArray currArray)
        {
            WalkArray(path, prevArray, currArray, changes);
            return;
        }

        if (!NodesEqual(previous, current))
        {
            changes.Add(new SettingsChangeEntry(path, ToLiteral(previous), ToLiteral(current)));
        }
    }

    private static void WalkObject(string path, JsonObject previousObject, JsonObject currentObject, List<SettingsChangeEntry> changes)
    {
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonNode?> kvp in previousObject)
        {
            _ = keys.Add(kvp.Key);
        }
        foreach (KeyValuePair<string, JsonNode?> kvp in currentObject)
        {
            _ = keys.Add(kvp.Key);
        }
        foreach (string key in keys.OrderBy(static k => k, StringComparer.Ordinal))
        {
            _ = previousObject.TryGetPropertyValue(key, out JsonNode? prevChild);
            _ = currentObject.TryGetPropertyValue(key, out JsonNode? currChild);
            string childPath = string.IsNullOrEmpty(path) ? key : path + "." + key;
            Walk(childPath, prevChild, currChild, changes);
        }
    }

    private static void WalkArray(string path, JsonArray prevArray, JsonArray currArray, List<SettingsChangeEntry> changes)
    {
        int max = Math.Max(prevArray.Count, currArray.Count);
        for (int i = 0; i < max; i++)
        {
            JsonNode? prevItem = i < prevArray.Count ? prevArray[i] : null;
            JsonNode? currItem = i < currArray.Count ? currArray[i] : null;
            string indexPath = path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            Walk(indexPath, prevItem, currItem, changes);
        }
    }

    private static bool NodesEqual(JsonNode? a, JsonNode? b)
    {
        if (a is null && b is null)
        {
            return true;
        }
        if (a is null || b is null)
        {
            return false;
        }
        // JsonValue → ToJsonString liefert kanonische Form (z. B. true/false/Number/"text").
        return string.Equals(a.ToJsonString(), b.ToJsonString(), StringComparison.Ordinal);
    }

    private static string? ToLiteral(JsonNode? node) => node?.ToJsonString(LiteralOptions);
}
