using System.Text.Json;
using System.Text.Json.Serialization;

namespace MdExplorer.Core.Settings;

/// <summary>
/// Die eine Form, in der Einstellungen als JSON geschrieben werden.
/// </summary>
/// <remarks>
/// Datei, Verlauf und Diff müssen dieselben Namen und Enum-Schreibweisen sehen. Eine zweite
/// Kopie der Optionen, etwa in den Tests, liefe auseinander, sobald sich hier etwas ändert, und
/// der Diff verglich dann zwei verschiedene Schreibweisen desselben Stands.
/// </remarks>
internal static class SettingsJson
{
    /// <summary>Optionen für Datei, Schnappschuss und Diff.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
