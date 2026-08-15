using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace MdExplorer.App.Tests;

/// <summary>
/// Hält die Lesbarkeit in beiden Belegungen.
/// </summary>
/// <remarks>
/// Kontrast ist die einzige Zusicherung der Gestaltungslinie, die man ausrechnen kann — und
/// die man deshalb nicht dem Augenmaß überlassen muss. Ein Blick auf den Bildschirm findet
/// eine Paarung, die knapp unter der Grenze liegt, verlässlich nicht; diese Prüfung schon.
/// Gerechnet wird nach WCAG 2.1: relative Leuchtdichte, Verhältnis (heller + 0,05) zu
/// (dunkler + 0,05).
/// </remarks>
public sealed class KontrastGuardTests
{
    /// <summary>Mindestverhältnis für Text.</summary>
    private const double TextMinimum = 4.5;

    /// <summary>Mindestverhältnis für Bedienelemente — auch im ruhenden Zustand.</summary>
    private const double ControlMinimum = 3.0;

    public static TheoryData<string, string, string, double> TextPairs => new()
    {
        { "Light.xaml", "TextPrimaryBrush", "AppBackgroundBrush", TextMinimum },
        { "Light.xaml", "TextPrimaryBrush", "SurfaceBrush", TextMinimum },
        { "Light.xaml", "TextSecondaryBrush", "SurfaceBrush", TextMinimum },
        { "Light.xaml", "ButtonForegroundBrush", "ButtonBackgroundBrush", TextMinimum },
        { "Light.xaml", "TextPrimaryBrush", "SelectionBackgroundBrush", TextMinimum },
        { "Light.xaml", "StatusBarForegroundBrush", "StatusBarBackgroundBrush", TextMinimum },
        { "Dark.xaml", "TextPrimaryBrush", "AppBackgroundBrush", TextMinimum },
        { "Dark.xaml", "TextPrimaryBrush", "SurfaceBrush", TextMinimum },
        { "Dark.xaml", "TextSecondaryBrush", "SurfaceBrush", TextMinimum },
        { "Dark.xaml", "ButtonForegroundBrush", "ButtonBackgroundBrush", TextMinimum },
        { "Dark.xaml", "TextPrimaryBrush", "SelectionBackgroundBrush", TextMinimum },
        { "Dark.xaml", "StatusBarForegroundBrush", "StatusBarBackgroundBrush", TextMinimum },
    };

    public static TheoryData<string, string, string, double> ControlPairs => new()
    {
        // Der Umriss eines Bedienelements zählt im ruhenden Zustand mit: Ein Feld, das man
        // erst findet, wenn man es angesteuert hat, ist zu spät gefunden.
        { "Light.xaml", "InputBorderBrush", "SurfaceBrush", ControlMinimum },
        { "Light.xaml", "InputBorderFocusBrush", "SurfaceBrush", ControlMinimum },
        { "Light.xaml", "AccentPrimaryBrush", "AppBackgroundBrush", ControlMinimum },
        { "Dark.xaml", "InputBorderBrush", "SurfaceBrush", ControlMinimum },
        { "Dark.xaml", "InputBorderFocusBrush", "SurfaceBrush", ControlMinimum },
        { "Dark.xaml", "AccentPrimaryBrush", "AppBackgroundBrush", ControlMinimum },
    };

    [Theory]
    [MemberData(nameof(TextPairs))]
    public void TextIsReadableOnItsGround(string palette, string foreground, string background, double minimum) =>
        AssertContrast(palette, foreground, background, minimum);

    [Theory]
    [MemberData(nameof(ControlPairs))]
    public void ControlsStandOutFromTheirGround(string palette, string foreground, string background, double minimum) =>
        AssertContrast(palette, foreground, background, minimum);

    private static void AssertContrast(string palette, string foregroundKey, string backgroundKey, double minimum)
    {
        Dictionary<string, string> colors = ColorsOf(palette);

        Assert.True(colors.ContainsKey(foregroundKey), $"{palette} kennt {foregroundKey} nicht.");
        Assert.True(colors.ContainsKey(backgroundKey), $"{palette} kennt {backgroundKey} nicht.");

        double ratio = ContrastRatio(colors[foregroundKey], colors[backgroundKey]);

        Assert.True(
            ratio >= minimum,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{palette}: {foregroundKey} auf {backgroundKey} erreicht nur {ratio:F2}:1, "
                + $"nötig sind {minimum:F1}:1."));
    }

    private static double ContrastRatio(string first, string second)
    {
        double one = RelativeLuminance(first);
        double other = RelativeLuminance(second);
        double lighter = Math.Max(one, other);
        double darker = Math.Min(one, other);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        string value = hex.TrimStart('#');
        // Acht Stellen tragen vorn die Deckkraft; für die Leuchtdichte zählt der Farbanteil.
        if (value.Length == 8)
        {
            value = value[2..];
        }

        double red = Channel(value[..2]);
        double green = Channel(value.Substring(2, 2));
        double blue = Channel(value.Substring(4, 2));

        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private static double Channel(string component)
    {
        double raw = int.Parse(component, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;

        return raw <= 0.03928 ? raw / 12.92 : Math.Pow((raw + 0.055) / 1.055, 2.4);
    }

    private static Dictionary<string, string> ColorsOf(string paletteFile)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string path = Path.Combine(SourceDirectory(), "MdExplorer.App", "Themes", paletteFile);

        return XDocument.Load(path)
            .Descendants()
            .Where(element => element.Attribute(x + "Key") is not null && element.Attribute("Color") is not null)
            .ToDictionary(
                element => element.Attribute(x + "Key")!.Value,
                element => element.Attribute("Color")!.Value,
                StringComparer.Ordinal);
    }

    private static string SourceDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MdExplorer.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return Path.Combine(directory.FullName, "src");
    }
}
