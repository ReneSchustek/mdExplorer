using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MdExplorer.App.Tests;

/// <summary>
/// Hält die Gestaltungslinie.
/// </summary>
/// <remarks>
/// Eine Gestaltungslinie, die nur in einem Dokument steht, hält kein Jahr: Nach dem dritten
/// „nur hier einmal schnell" ist sie Papier. Diese Prüfungen machen den Build rot, statt auf
/// gutes Zureden zu hoffen.
/// </remarks>
public sealed class GestaltungslinieGuardTests
{
    // Farbwerte gehören in die Belegungen, nicht in eine Ansicht. „Transparent" ist keine
    // Farbe der Marke, sondern eine Aussage über die Fläche — deshalb erlaubt.
    private static readonly Regex ColorAttribute = new(
        "(Background|Foreground|BorderBrush|Fill|Stroke|CaretBrush|SelectionBrush|Color)\\s*=\\s*"
        + "\"(#[0-9A-Fa-f]{3,8}|White|Black|Gray|LightGray|DarkGray|Silver|Red|Green|Blue|Yellow|Orange|Navy|Teal)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Zweiter Weg in dieselbe Falle: Ein Setter trägt die Farbe im Value, nicht im
    // Attributnamen. Ohne diese Prüfung bliebe jede Farbe in einem Style unentdeckt —
    // im Bestand waren es vier.
    // Verweise auf Ressourcen — beide Formen. Statisch für die Bausteine, dynamisch für
    // die Farben, die beim Wechsel des Erscheinungsbilds getauscht werden.
    private static readonly Regex ResourceReference = new(
        "\\{(?:DynamicResource|StaticResource)\\s+(?<key>[A-Za-z0-9_.]+)\\s*\\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Nur die statische Form: Ein dynamischer Verweis wird erst beim Anzeigen aufgelöst und
    // darf deshalb auf einen Schlüssel zeigen, der weiter unten steht.
    private static readonly Regex StaticResourceReference = new(
        "\\{StaticResource\\s+(?<key>[A-Za-z0-9_.]+)\\s*\\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ColorSetterValue = new(
        "Value\\s*=\\s*\"(#[0-9A-Fa-f]{3,8}|White|Black|Gray|LightGray|DarkGray|Silver|Red|Green|Blue|Yellow|Orange|Navy|Teal)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Dritter Weg in dieselbe Falle, und der stillste: Die Farbe steht gar nicht in der
    // Ansicht, sondern im Code dahinter. Die Prüfungen oben lesen XAML und haben deshalb
    // ein halbes Jahr lang nicht gesehen, dass die Trefferhervorhebung der Suche einen
    // festen Gelbton mitbrachte — im Dunklen heller Text auf hellem Grund.
    private static readonly Regex ColorInCode = new(
        "Color\\.From(Rgb|Argb|Scrgb)\\s*\\(|Colors\\.[A-Z][A-Za-z]+|Brushes\\.[A-Z][A-Za-z]+"
        + "|\"#[0-9A-Fa-f]{3,8}\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void ViewsAndControlsContainNoColorValues()
    {
        List<string> findings = [];

        foreach (string file in ViewAndControlFiles())
        {
            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                Match attribute = ColorAttribute.Match(lines[index]);
                if (attribute.Success)
                {
                    findings.Add($"{Path.GetFileName(file)}:{index + 1}  {attribute.Value.Trim()}");
                }

                Match setter = ColorSetterValue.Match(lines[index]);
                if (setter.Success)
                {
                    findings.Add($"{Path.GetFileName(file)}:{index + 1}  {setter.Value.Trim()}");
                }
            }
        }

        Assert.True(
            findings.Count == 0,
            "Farbwerte gehören in Themes/Light.xaml und Themes/Dark.xaml, nicht in eine Ansicht. "
            + "Sonst bleibt die Stelle beim Wechsel des Erscheinungsbilds stehen:"
            + Environment.NewLine + string.Join(Environment.NewLine, findings));
    }

    /// <remarks>
    /// Fehlt ein Schlüssel in einer Belegung, bricht die Bindung genau in dieser Belegung —
    /// und das fällt erst beim Nutzer auf, nicht beim Entwickeln.
    /// </remarks>
    [Fact]
    public void LightAndDarkDefineTheSameKeys()
    {
        HashSet<string> light = ResourceKeys("Light.xaml");
        HashSet<string> dark = ResourceKeys("Dark.xaml");

        List<string> onlyLight = [.. light.Except(dark).Order()];
        List<string> onlyDark = [.. dark.Except(light).Order()];

        Assert.True(
            onlyLight.Count == 0 && onlyDark.Count == 0,
            "Light.xaml und Dark.xaml müssen denselben Schlüsselsatz führen."
            + Environment.NewLine + "Nur in Light: " + string.Join(", ", onlyLight)
            + Environment.NewLine + "Nur in Dark: " + string.Join(", ", onlyDark));
    }

    /// <remarks>
    /// Eine Ansicht ist nicht nur ihr Markup. Wer die Farbe eine Ebene tiefer schreibt —
    /// in den Code hinter der Ansicht oder in einen Wandler —, umgeht jede Prüfung, die
    /// XAML liest. Genau so kam die Trefferhervorhebung der Suche zu ihrem festen Gelbton,
    /// und genau deshalb war sie im dunklen Erscheinungsbild nicht mehr lesbar.
    /// </remarks>
    [Fact]
    public void CodeBehindAndConvertersContainNoColorValues()
    {
        List<string> findings = [];

        foreach (string file in CodeFilesOfPresentationLayer())
        {
            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                Match match = ColorInCode.Match(lines[index]);
                if (match.Success)
                {
                    findings.Add($"{Path.GetFileName(file)}:{index + 1}  {lines[index].Trim()}");
                }
            }
        }

        Assert.True(
            findings.Count == 0,
            "Auch im Code hinter einer Ansicht gehören Farben in die Belegung. Ein fester Wert "
            + "bleibt beim Wechsel des Erscheinungsbilds stehen — über SetResourceReference "
            + "zieht er mit:"
            + Environment.NewLine + string.Join(Environment.NewLine, findings));
    }

    /// <remarks>
    /// Ohne diese Prüfung wäre die Farbsuche oben grün, sobald sie keine Datei mehr findet —
    /// etwa nach einer Umbenennung des Verzeichnisses. Ein Wächter, der nichts mehr ansieht,
    /// meldet dasselbe wie einer, der nichts findet.
    /// </remarks>
    [Fact]
    public void PalettesAndViewsAreNotEmpty()
    {
        Assert.NotEmpty(ResourceKeys("Light.xaml"));
        Assert.NotEmpty(ViewAndControlFiles());
        Assert.NotEmpty(CodeFilesOfPresentationLayer());
    }

    /// <remarks>
    /// Die Bausteine der Linie stehen in Tokens.xaml und gelten in beiden Belegungen.
    /// Wer sie versehentlich in eine Belegung schiebt, bekommt sie beim Wechsel getauscht —
    /// dann ändert sich mit dem Erscheinungsbild plötzlich der Abstand.
    /// </remarks>
    [Fact]
    public void PalettesCarryNoSpacingOrTypography()
    {
        string[] tokenKeys = ["Spacing", "Padding", "CornerRadius", "FontSize", "FontFamily"];

        List<string> findings =
        [
            .. ResourceKeys("Light.xaml").Concat(ResourceKeys("Dark.xaml"))
                .Where(key => tokenKeys.Any(token => key.Contains(token, StringComparison.Ordinal)))
                .Distinct()
                .Order()
        ];

        Assert.True(
            findings.Count == 0,
            "Abstände, Radien und Typografie gehören nach Themes/Tokens.xaml — sie wechseln "
            + "mit dem Erscheinungsbild nicht: " + string.Join(", ", findings));
    }

    /// <remarks>
    /// Der stillste Fehler von allen: Ein Verweis auf einen Schlüssel, den es nicht gibt,
    /// wirft zur Laufzeit nicht — die Stelle bleibt einfach leer. Im hellen Erscheinungsbild
    /// fällt eine fehlende Randfarbe kaum auf, im dunklen ist die Stelle weg. Kein Build und
    /// kein Blick auf den Bildschirm findet das verlässlich; diese Prüfung schon.
    /// </remarks>
    [Fact]
    public void EveryReferencedResourceKeyExists()
    {
        // Die Linie liefert Bausteine, Farben und die Grundbelegung der Bedienelemente;
        // Konverter und Listen stehen in App.xaml oder in der Ansicht selbst. Alles davon
        // zählt als vorhanden — gesucht wird der Verweis, den keine dieser Quellen kennt.
        HashSet<string> shared =
        [
            .. ResourceKeys("Tokens.xaml"),
            .. ResourceKeys("Light.xaml"),
            .. ResourceKeys("Dark.xaml"),
            .. ResourceKeys("ControlStyles.xaml"),
            .. KeysOf(Path.Combine(AppProjectDirectory(), "App.xaml")),
        ];

        List<string> findings = [];

        foreach (string file in ViewAndControlFiles())
        {
            HashSet<string> available = [.. shared, .. KeysOf(file)];
            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                foreach (Match match in ResourceReference.Matches(lines[index]))
                {
                    string key = match.Groups["key"].Value;
                    if (!available.Contains(key))
                    {
                        findings.Add($"{Path.GetFileName(file)}:{index + 1}  {key}");
                    }
                }
            }
        }

        Assert.True(
            findings.Count == 0,
            "Diese Verweise zeigen auf Schlüssel, die in keiner Belegung und in keinem Baustein "
            + "stehen. Zur Laufzeit bleibt die Stelle leer, ohne Fehlermeldung:"
            + Environment.NewLine + string.Join(Environment.NewLine, findings));
    }

    /// <remarks>
    /// Alle Ansichten, nicht nur die des Hauptmoduls: Die Tag-Verwaltung liegt in einem
    /// eigenen Modul und trug deshalb ein halbes Jahr lang feste Farbwerte, ohne dass diese
    /// Prüfung sie je angesehen hätte. Ein Wächter, der nur den halben Bestand kennt,
    /// meldet dasselbe wie einer, der nichts findet.
    /// </remarks>
    /// <remarks>
    /// Der Fehler, den die vorige Prüfung nicht sieht: Der Schlüssel <b>existiert</b> in der
    /// Datei, steht aber hinter seiner Verwendung. WPF löst einen statischen Verweis beim
    /// Lesen auf — was danach kommt, kennt es an dieser Stelle noch nicht. Das Fenster wirft
    /// dann beim Öffnen, und zwar erst zur Laufzeit: Bau und Tests bleiben grün. Genau so lag
    /// die Tag-Verwaltung lahm, ohne dass es jemandem auffiel.
    /// </remarks>
    [Fact]
    public void StaticResourcesAreDefinedBeforeTheyAreUsed()
    {
        List<string> findings = [];

        foreach (string file in ViewAndControlFiles())
        {
            string content = File.ReadAllText(file);
            HashSet<string> localKeys = KeysOf(file);

            foreach (Match match in StaticResourceReference.Matches(content))
            {
                string key = match.Groups["key"].Value;
                if (!localKeys.Contains(key))
                {
                    // Der Schlüssel kommt von außen — dann gilt die Reihenfolge nicht.
                    continue;
                }

                int definition = content.IndexOf($"x:Key=\"{key}\"", StringComparison.Ordinal);
                if (definition > match.Index)
                {
                    findings.Add($"{Path.GetFileName(file)}: {key} wird verwendet, bevor es definiert ist.");
                }
            }
        }

        Assert.True(
            findings.Count == 0,
            "Ein statischer Verweis auf einen Schlüssel, der erst später in derselben Datei steht, "
            + "wirft beim Öffnen der Ansicht — nicht beim Bauen:"
            + Environment.NewLine + string.Join(Environment.NewLine, findings));
    }

    private static List<string> ViewAndControlFiles()
    {
        List<string> files = [];

        foreach (string module in Directory.EnumerateDirectories(SourceDirectory()))
        {
            foreach (string folder in new[] { "Views", "Controls" })
            {
                string path = Path.Combine(module, folder);
                if (Directory.Exists(path))
                {
                    files.AddRange(Directory.EnumerateFiles(path, "*.xaml", SearchOption.AllDirectories));
                }
            }
        }

        return files;
    }

    /// <summary>
    /// Alle C#-Dateien der Darstellungsschicht: Code hinter den Ansichten, die Bausteine
    /// und die Wandler.
    /// </summary>
    private static List<string> CodeFilesOfPresentationLayer()
    {
        List<string> files = [];

        foreach (string module in Directory.EnumerateDirectories(SourceDirectory()))
        {
            foreach (string folder in new[] { "Views", "Controls", "Converters" })
            {
                string path = Path.Combine(module, folder);
                if (Directory.Exists(path))
                {
                    files.AddRange(Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories));
                }
            }
        }

        return files;
    }

    private static HashSet<string> ResourceKeys(string paletteFile) =>
        KeysOf(Path.Combine(AppProjectDirectory(), "Themes", paletteFile));

    /// <summary>Liest alle <c>x:Key</c>-Namen einer XAML-Datei.</summary>
    private static HashSet<string> KeysOf(string path)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        return
        [
            .. XDocument.Load(path)
                .Descendants()
                .Select(element => element.Attribute(x + "Key")?.Value)
                .Where(key => key is not null)
                .Select(key => key!)
        ];
    }

    private static string AppProjectDirectory() => Path.Combine(SourceDirectory(), "MdExplorer.App");

    // Vom Testausgabeverzeichnis nach oben, bis die Projektmappe auftaucht. So bleibt der
    // Pfad unabhängig davon, wo das Repository liegt.
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
