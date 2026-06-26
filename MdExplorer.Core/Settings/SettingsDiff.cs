using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MdExplorer.Core.Settings;

/// <summary>
/// Strukturierter JSON-Diff zwischen zwei <see cref="MdExplorer.Core.Models.AppSettings"/>-
/// Snapshots. Trifft pro geänderter Property einen <see cref="SettingsChangeEntry"/>.
/// Rein funktional — kein State, keine Seiteneffekte.
/// </summary>
public static class SettingsDiff
{
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
        if (previous is JsonObject prevObj && current is JsonObject currObj)
        {
            HashSet<string> keys = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonNode?> kvp in prevObj)
            {
                _ = keys.Add(kvp.Key);
            }
            foreach (KeyValuePair<string, JsonNode?> kvp in currObj)
            {
                _ = keys.Add(kvp.Key);
            }
            foreach (string key in keys.OrderBy(static k => k, StringComparer.Ordinal))
            {
                _ = prevObj.TryGetPropertyValue(key, out JsonNode? prevChild);
                _ = currObj.TryGetPropertyValue(key, out JsonNode? currChild);
                string childPath = string.IsNullOrEmpty(path) ? key : path + "." + key;
                Walk(childPath, prevChild, currChild, changes);
            }
            return;
        }

        if (previous is JsonArray prevArray && current is JsonArray currArray)
        {
            int max = Math.Max(prevArray.Count, currArray.Count);
            for (int i = 0; i < max; i++)
            {
                JsonNode? prevItem = i < prevArray.Count ? prevArray[i] : null;
                JsonNode? currItem = i < currArray.Count ? currArray[i] : null;
                string indexPath = path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                Walk(indexPath, prevItem, currItem, changes);
            }
            return;
        }

        if (!NodesEqual(previous, current))
        {
            changes.Add(new SettingsChangeEntry(path, ToLiteral(previous), ToLiteral(current)));
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

    private static string? ToLiteral(JsonNode? node) => node?.ToJsonString();

    /// <summary>
    /// Serialisiert einen <see cref="MdExplorer.Core.Models.AppSettings"/>-Snapshot in das
    /// Audit-kompatible JSON-Format. Nutzt CamelCase und überspringt <c>null</c>-Werte —
    /// identisch zur Wire-Format-Datei <c>settings.json</c>.
    /// </summary>
    /// <typeparam name="T">Typ des zu serialisierenden Snapshots — meist <see cref="MdExplorer.Core.Models.AppSettings"/>.</typeparam>
    /// <param name="value">Snapshot-Instanz, die serialisiert wird.</param>
    /// <param name="options">JSON-Optionen (CamelCase, Indent, Converter) fuer das Wire-Format.</param>
    /// <returns>Serialisierte JSON-Repraesentation von <paramref name="value"/>.</returns>
    public static string Serialize<T>(T value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);
        return JsonSerializer.Serialize(value, options);
    }
}
