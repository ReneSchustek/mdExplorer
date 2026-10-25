using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace MdExplorer.App.Tests;

/// <summary>
/// Hält die Lesbarkeit in den Stylesheets von Vorschau, Hilfe und Graph, in beiden Belegungen.
/// </summary>
/// <remarks>
/// Die Farben stehen dort als Variablen, hell im ersten Block, dunkel im Block mit
/// <c>data-theme="dark"</c>. Geprüft wird je Belegung jedes Paar aus Schrift und Grund, auf dem
/// sie steht. Halbtransparente Farben fehlen hier; ihr Kontrast hängt vom Grund darunter ab.
/// </remarks>
public sealed partial class CssContrastGuardTests
{
    private const double TextMinimum = 4.5;

    public static TheoryData<string, string, string> Pairs => new()
    {
        { "preview.css", "--preview-text", "--preview-background" },
        { "preview.css", "--preview-muted", "--preview-background" },
        { "preview.css", "--preview-link", "--preview-background" },
        { "preview.css", "--preview-text", "--preview-code-background" },
        { "preview.css", "--preview-mark-text", "--preview-mark-background" },
        { "Help/help.css", "--help-text", "--help-background" },
        { "Help/help.css", "--help-muted", "--help-background" },
        { "Help/help.css", "--help-link", "--help-background" },
        { "Help/help.css", "--help-text", "--help-code-background" },
        { "Help/help.css", "--help-text", "--help-hit-background" },
        { "Graph/graph.css", "--graph-label", "--graph-canvas" },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void TextIsReadableInBothThemes(string stylesheet, string foreground, string background)
    {
        ArgumentNullException.ThrowIfNull(stylesheet);
        string css = File.ReadAllText(Path.Combine(RepositoryPaths.Project("MdExplorer.App"), "Assets", stylesheet));
        List<Dictionary<string, string>> themes = [.. VariableBlock().Matches(css).Select(ColorsOf)];

        Assert.Equal(2, themes.Count);
        foreach (Dictionary<string, string> colors in themes)
        {
            Assert.True(colors.ContainsKey(foreground) && colors.ContainsKey(background), $"{stylesheet}: {foreground} oder {background} fehlt.");
            double ratio = ContrastGuardTests.ContrastRatio(colors[foreground], colors[background]);
            Assert.True(
                ratio >= TextMinimum,
                string.Create(CultureInfo.InvariantCulture, $"{stylesheet}: {foreground} auf {background} erreicht nur {ratio:F2}:1."));
        }
    }

    private static Dictionary<string, string> ColorsOf(Match block) =>
        Variable().Matches(block.Groups["body"].Value)
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["color"].Value, StringComparer.Ordinal);

    // Ein Block, der Variablen festlegt: „:root {", „:root[data-theme="dark"] {", „body {" usw.
    [GeneratedRegex(@"^[^{}\n]*\{(?<body>[^}]*--[a-z-]+\s*:[^}]*)\}", RegexOptions.Multiline)]
    private static partial Regex VariableBlock();

    [GeneratedRegex(@"(?<name>--[a-z-]+)\s*:\s*(?<color>#[0-9A-Fa-f]{6})\s*;")]
    private static partial Regex Variable();
}
