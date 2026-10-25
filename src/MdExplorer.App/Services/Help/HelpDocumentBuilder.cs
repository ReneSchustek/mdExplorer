using System.Net;
using MdExplorer.App.Localization;

namespace MdExplorer.App.Services.Help;

/// <summary>
/// Baut das Dokument des Hilfefensters aus den eingebetteten Vorlagen und dem gerenderten
/// Handbuch.
/// </summary>
internal static class HelpDocumentBuilder
{
    private const string AssetPrefix = "MdExplorer.App.Assets.Help.";
    private const string HtmlPlaceholder = "__HELP_HTML__";

    /// <summary>Baut das vollständige Dokument.</summary>
    /// <param name="helpHtml">Das gerenderte Handbuch.</param>
    /// <param name="isDarkMode">Ob das dunkle Erscheinungsbild gilt.</param>
    public static string Build(string helpHtml, bool isDarkMode)
    {
        ArgumentNullException.ThrowIfNull(helpHtml);

        KeyValuePair<string, string>[] parts =
        [
            new("__INLINE_CSS__", EmbeddedDocument.ReadAsset(AssetPrefix + "help.css")),
            new("__INLINE_JS__", EmbeddedDocument.ReadAsset(AssetPrefix + "help.js")),
            new("__THEME__", isDarkMode ? "dark" : "light"),
            new("__TITLE__", WebUtility.HtmlEncode(UiText.Get("Help_Title"))),
        ];

        return EmbeddedDocument.Fill(EmbeddedDocument.ReadAsset(AssetPrefix + "help.html"), parts, HtmlPlaceholder, helpHtml);
    }
}
