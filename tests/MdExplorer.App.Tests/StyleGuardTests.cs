using System.IO;
using System.Xml.Linq;

namespace MdExplorer.App.Tests;

/// <summary>
/// Fenster und lokale Styles bauen auf den Styles der Anwendung auf, statt sie zu ersetzen.
/// </summary>
/// <remarks>
/// <para>
/// Ein Style ohne Schlüssel greift nur beim genau passenden Typ. Ein Fenster ist immer ein
/// abgeleiteter Typ und bekommt ihn deshalb nur über einen ausdrücklichen Verweis.
/// </para>
/// <para>
/// Ein lokaler Style ohne <c>BasedOn</c> verdrängt den der Anwendung samt Vorlage. Das Element
/// fällt dann auf die Voreinstellung des Rahmenwerks zurück und steht im dunklen
/// Erscheinungsbild hell da.
/// </para>
/// </remarks>
public sealed class StyleGuardTests
{
    private const string WindowStyleReference = "{StaticResource {x:Type Window}}";

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] ModuleNames = ["MdExplorer.App", "MdExplorer.TagCloud"];

    // Das Startbild trägt die Farben der Marke, nicht die der Belegung: Es steht, bevor die
    // Einstellungen gelesen sind.
    private static readonly string[] WindowsWithOwnLook = ["SplashWindow.xaml"];

    [Fact]
    public void EveryWindowReferencesTheAppWindowStyle()
    {
        List<string> windows = [.. ViewFiles().Where(IsWindow)];
        List<string> missing =
        [
            .. windows
                .Where(file => !WindowsWithOwnLook.Contains(Path.GetFileName(file), StringComparer.Ordinal))
                .Where(file => (string?)XDocument.Load(file).Root!.Attribute("Style") != WindowStyleReference)
                .Select(file => Path.GetRelativePath(RepositoryPaths.Root(), file)),
        ];

        Assert.NotEmpty(windows);
        Assert.True(missing.Count == 0, "Ohne Verweis auf den Fenster-Style: " + string.Join(", ", missing));
    }

    [Fact]
    public void LocalStylesBuildOnTheAppStyles()
    {
        HashSet<string> styledByTheApp = ImplicitlyStyledTypes();
        List<string> findings = [];

        foreach (string file in ViewFiles())
        {
            foreach (XElement style in XDocument.Load(file, LoadOptions.SetLineInfo).Descendants().Where(element => element.Name.LocalName == "Style"))
            {
                string? target = TargetTypeName(style);
                if (target is not null && styledByTheApp.Contains(target) && style.Attribute("BasedOn") is null)
                {
                    findings.Add($"{Path.GetRelativePath(RepositoryPaths.Root(), file)}:{((System.Xml.IXmlLineInfo)style).LineNumber} ({target})");
                }
            }
        }

        Assert.NotEmpty(styledByTheApp);
        Assert.True(findings.Count == 0, "Lokaler Style ohne BasedOn: " + string.Join(", ", findings));
    }

    private static HashSet<string> ImplicitlyStyledTypes()
    {
        string controlStyles = Path.Combine(RepositoryPaths.Project("MdExplorer.App"), "Themes", "ControlStyles.xaml");

        return
        [
            .. XDocument.Load(controlStyles).Root!.Elements()
                .Where(element => element.Name.LocalName == "Style" && element.Attribute(Xaml + "Key") is null)
                .Select(TargetTypeName)
                .OfType<string>(),
        ];
    }

    // „Button" und „{x:Type Button}" meinen dasselbe.
    private static string? TargetTypeName(XElement style)
    {
        string? target = (string?)style.Attribute("TargetType");
        return target?.Replace("{x:Type ", string.Empty, StringComparison.Ordinal).TrimEnd('}').Trim();
    }

    private static bool IsWindow(string file) => XDocument.Load(file).Root!.Name.LocalName == "Window";

    private static IEnumerable<string> ViewFiles() =>
        ModuleNames
            .SelectMany(module => RepositoryPaths.SourceFiles(module, "*.xaml"))
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Contains("Themes"));
}
