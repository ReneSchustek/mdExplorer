using System.Net;
using System.Text.Json;
using MdExplorer.App.Localization;

namespace MdExplorer.App.Services;

/// <summary>
/// Baut das Dokument für die Zeichenfläche des Graph-Fensters aus den eingebetteten Vorlagen.
/// Die Belegung geht als Kennzeichen am Wurzelelement mit; die Farben stehen im Stylesheet, nicht
/// im Zeichencode.
/// </summary>
internal static class GraphDocumentBuilder
{
    private const string AssetPrefix = "MdExplorer.App.Assets.Graph.";
    private const string JsonPlaceholder = "__GRAPH_JSON__";

    // Ein „<" im Datenblock wird maskiert. JSON kennt „<" nur in Zeichenketten, und dort bedeutet
    // die Maskierung dasselbe Zeichen; ein „</script>" aus einem Titel beendet den Block so nicht.
    private const string EscapedLessThan = "\\" + "u003c";

    /// <summary>Baut das vollständige Dokument.</summary>
    /// <param name="snapshotJson">Knoten und Kanten als JSON.</param>
    /// <param name="isDarkMode">Ob das dunkle Erscheinungsbild gilt.</param>
    public static string Build(string snapshotJson, bool isDarkMode)
    {
        ArgumentNullException.ThrowIfNull(snapshotJson);

        string texts = JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["legend"] = UiText.Get("Graph_Legend_Format"),
            ["canvasName"] = UiText.Get("Graph_Canvas_Name"),
        });

        KeyValuePair<string, string>[] parts =
        [
            new("__INLINE_CSS__", EmbeddedDocument.ReadAsset(AssetPrefix + "graph.css")),
            new("__INLINE_JS__", EmbeddedDocument.ReadAsset(AssetPrefix + "graph.js")),
            new("__THEME__", isDarkMode ? "dark" : "light"),
            new("__TITLE__", WebUtility.HtmlEncode(UiText.Get("Graph_Title"))),
            new("__GRAPH_TEXTS__", Mask(texts)),
        ];

        return EmbeddedDocument.Fill(EmbeddedDocument.ReadAsset(AssetPrefix + "graph.html"), parts, JsonPlaceholder, Mask(snapshotJson));
    }

    private static string Mask(string json) => json.Replace("<", EscapedLessThan, StringComparison.Ordinal);
}
