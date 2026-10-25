using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MdExplorer.App.Tests;

/// <summary>
/// Jedes Bedienelement hat für Screenreader einen Namen, und klickbar ist nur, was auch per
/// Tastatur erreichbar ist.
/// </summary>
/// <remarks>
/// <para>
/// Ein Knopf mit „✕“ liest der Screenreader als „Kreuz“ vor, nicht als „Schließen“. Ein Feld,
/// neben dem die Beschriftung nur optisch steht, ist für ihn namenlos. Eine Ellipse mit
/// Mausereignis hat weder Rolle noch Tastaturfokus.
/// </para>
/// <para>
/// Ein Feld gilt als benannt, wenn es <c>AutomationProperties.Name</c> trägt oder über
/// <c>AutomationProperties.LabeledBy</c> auf seine Beschriftung zeigt. <c>Label.Target</c>
/// allein reicht nicht: Am laufenden Programm meldete das Feld dann einen leeren Namen.
/// </para>
/// </remarks>
public sealed partial class AccessibilityGuardTests
{
    private static readonly string[] ModuleNames = ["MdExplorer.App", "MdExplorer.TagCloud"];

    private static readonly string[] ButtonTypes = ["Button", "ToggleButton", "RepeatButton"];

    private static readonly string[] InputTypes = ["TextBox", "ComboBox", "PasswordBox"];

    private static readonly string[] MouseEvents = ["MouseLeftButtonUp", "MouseLeftButtonDown", "MouseDown", "MouseUp"];

    [Fact]
    public void SymbolButtonsCarryAName() =>
        AssertNoFindings("Symbol-Knopf ohne AutomationProperties.Name", SymbolButtonGap);

    [Fact]
    public void InputFieldsCarryANameOrALabel() =>
        AssertNoFindings("Eingabefeld ohne Namen und ohne LabeledBy", InputGap);

    [Fact]
    public void ListEntriesCarryAName() =>
        AssertNoFindings("Liste ohne AutomationProperties.Name je Eintrag", ListGap);

    [Fact]
    public void OnlyRealControlsReactToClicks() =>
        AssertNoFindings("Mausereignis an einem Element ohne Rolle und Tastaturfokus", ClickGap);

    // Gegenproben: Ohne sie stünde der Wächter auch dann grün, wenn er gar nichts mehr träfe.
    [Theory]
    [InlineData("<Button Content=\"✕\" />", 1)]
    [InlineData("<Button Content=\"✕\" AutomationProperties.Name=\"{l:Loc Action_Close}\" />", 0)]
    [InlineData("<Button Content=\"{l:Loc Action_Close}\" />", 0)]
    [InlineData("<TextBox Text=\"{Binding Query}\" />", 1)]
    [InlineData("<TextBox AutomationProperties.LabeledBy=\"{Binding ElementName=QueryLabel}\" />", 0)]
    [InlineData("<Ellipse MouseLeftButtonUp=\"OnClick\" />", 1)]
    [InlineData("<ListBox ItemsSource=\"{Binding Items}\" AutomationProperties.Name=\"Liste\" DisplayMemberPath=\"Title\" />", 1)]
    [InlineData("<ListBox AutomationProperties.Name=\"Liste\"><ListBox.ItemContainerStyle><Style><Setter Property=\"AutomationProperties.Name\" Value=\"{Binding Title}\" /></Style></ListBox.ItemContainerStyle></ListBox>", 0)]
    [InlineData("<ComboBox Style=\"{StaticResource EnumComboBoxStyle}\" AutomationProperties.Name=\"Modus\" />", 0)]
    [InlineData("<ItemsControl ItemsSource=\"{Binding Filters}\" />", 1)]
    public void TheDetection_FindsExactlyTheGaps(string markup, int expected)
    {
        ArgumentNullException.ThrowIfNull(markup);
        XElement element = XElement.Parse(markup);

        int found = (SymbolButtonGap(element) ? 1 : 0) + (InputGap(element) ? 1 : 0) + (ClickGap(element) ? 1 : 0)
            + (ListGap(element) ? 1 : 0);

        Assert.Equal(expected, found);
    }

    private static bool SymbolButtonGap(XElement element) =>
        ButtonTypes.Contains(element.Name.LocalName)
        && element.Attribute("Content") is { } content
        && !content.Value.StartsWith('{')
        && !Word().IsMatch(content.Value)
        && !HasAccessibleName(element);

    private static bool InputGap(XElement element) =>
        InputTypes.Contains(element.Name.LocalName) && !IsTemplatePart(element) && !HasAccessibleName(element);

    private static bool ClickGap(XElement element) =>
        element.Name.LocalName is "Ellipse" or "Border" or "Rectangle" or "Grid" or "TextBlock" or "Image"
        && element.Attributes().Any(attribute => MouseEvents.Contains(attribute.Name.LocalName));

    // Ohne Namen je Eintrag liest der Screenreader den Typ des Datenobjekts vor. DisplayMemberPath
    // hilft dabei nicht; erst ein Setter im ItemContainerStyle gibt dem Eintrag einen Namen.
    private static bool ListGap(XElement element) =>
        element.Name.LocalName is "ListBox" or "ListView" or "TreeView" or "ComboBox" or "ItemsControl"
        && !IsTemplatePart(element)
        && !((string?)element.Attribute("Style") ?? string.Empty).Contains("EnumComboBoxStyle", StringComparison.Ordinal)
        // Feste Einträge mit eigenem Text sind über ihren Inhalt benannt.
        && !element.Elements().Any(child => child.Name.LocalName is "ComboBoxItem" or "ListBoxItem")
        && !element.Descendants().Any(child =>
            child.Name.LocalName == "Setter" && (string?)child.Attribute("Property") == "AutomationProperties.Name");

    private static bool HasAccessibleName(XElement element) =>
        element.Attributes().Any(attribute => attribute.Name.LocalName is "AutomationProperties.Name" or "AutomationProperties.LabeledBy")
        || element.Elements().Any(child => child.Name.LocalName is "AutomationProperties.Name" or "AutomationProperties.LabeledBy");

    // Ein Feld in einer Vorlage bekommt seinen Namen vom Element, dem die Vorlage gehört.
    private static bool IsTemplatePart(XElement element) =>
        element.Ancestors().Any(ancestor => ancestor.Name.LocalName is "ControlTemplate");

    private static void AssertNoFindings(string what, Func<XElement, bool> isGap)
    {
        string root = RepositoryPaths.Root();
        List<string> findings = [];

        foreach (string file in ModuleNames.SelectMany(module => RepositoryPaths.SourceFiles(module, "*.xaml")))
        {
            if (file.Split(Path.DirectorySeparatorChar).Contains("Themes"))
            {
                continue;
            }
            foreach (XElement element in XDocument.Load(file, LoadOptions.SetLineInfo).Descendants().Where(isGap))
            {
                findings.Add($"{Path.GetRelativePath(root, file)}:{((IXmlLineInfo)element).LineNumber} <{element.Name.LocalName}>");
            }
        }

        Assert.True(findings.Count == 0, what + ":" + Environment.NewLine + string.Join(Environment.NewLine, findings));
    }

    [GeneratedRegex(@"\p{L}{2,}")]
    private static partial Regex Word();
}
