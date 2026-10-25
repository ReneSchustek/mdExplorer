using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MdExplorer.App.Tests;

/// <summary>
/// Anzeigetexte stehen in <c>Strings/Resources.resx</c> der App und der Tag-Cloud, nicht im
/// Markup.
/// </summary>
/// <remarks>
/// <para>
/// Ein falsch geschriebener Schlüssel übersetzt ohne Fehler und zeigt erst in der Oberfläche
/// <c>[?Schlüssel?]</c>. Deshalb wird jeder verwendete Schlüssel gegen die Datei geprüft, und
/// umgekehrt jeder Eintrag auf Verwendung: Ein Text, der nirgends mehr erscheint, wird sonst
/// weiter gepflegt.
/// </para>
/// <para>
/// Die Suche nach festen Texten im Markup steht hier zusätzlich zum i18n-Prüfer der Werkstatt.
/// Der verwirft eine ganze Zeile, sobald darin ein ausgenommener Wert wie <c>True</c> steht, und
/// übersah so <c>Content="Anwenden" IsDefault="True"</c>.
/// </para>
/// </remarks>
public sealed partial class UiTextResourceGuardTests
{
    // Der Produktname bleibt in jeder Sprache gleich und gehört deshalb nicht in die Ressourcen.
    private static readonly string[] BrandNames = ["MdExplorer"];

    private static readonly string[] ModuleNames = ["MdExplorer.App", "MdExplorer.TagCloud"];

    public static TheoryData<string> Modules => [.. ModuleNames];

    [Theory]
    [MemberData(nameof(Modules))]
    public void TheEntries_AreSortedUniqueNamedAndFilled(string module)
    {
        List<KeyValuePair<string, string>> entries = Entries(module);
        List<string> keys = [.. entries.Select(entry => entry.Key)];

        Assert.Equal(keys.Order(StringComparer.Ordinal), keys);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys, key => Assert.Matches(KeyPattern(), key));
        Assert.All(entries, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), entry.Key));
        // Abstände sind Sache des Layouts. Ein Leerzeichen am Rand eines Textes setzt sich zum
        // Abstand der Elemente dazu und verschwindet in einer anderen Sprache.
        Assert.All(entries, entry => Assert.True(entry.Value.Trim() == entry.Value, $"{entry.Key} beginnt oder endet mit Leerraum."));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void EveryUsedKeyExists_AndEveryEntryIsUsed(string module)
    {
        HashSet<string> defined = [.. Entries(module).Select(entry => entry.Key)];
        HashSet<string> used = [];
        foreach (string file in RepositoryPaths.SourceFiles(module, "*.xaml").Concat(RepositoryPaths.SourceFiles(module, "*.cs")))
        {
            used.UnionWith(UsedKeys(File.ReadAllText(file)));
        }

        Assert.True(used.IsSubsetOf(defined), "Ohne Eintrag: " + string.Join(", ", used.Except(defined)));
        Assert.True(defined.IsSubsetOf(used), "Nirgends verwendet: " + string.Join(", ", defined.Except(used)));
    }

    [Fact]
    public void NoMarkupCarriesAFixedDisplayText()
    {
        string root = RepositoryPaths.Root();
        List<string> findings = [];

        foreach (string module in ModuleNames)
        {
            foreach (string file in RepositoryPaths.SourceFiles(module, "*.xaml").Where(file => !file.Contains("Themes", StringComparison.Ordinal)))
            {
                foreach (int line in FixedTexts(File.ReadAllLines(file)))
                {
                    findings.Add($"{Path.GetRelativePath(root, file)}:{line}");
                }
            }
        }

        Assert.True(
            findings.Count == 0,
            "Fester Anzeigetext im Markup statt {l:Loc …}:" + Environment.NewLine + string.Join(Environment.NewLine, findings));
    }

    [Fact]
    public void NoCodeBehindCarriesAFixedDisplayText()
    {
        string root = RepositoryPaths.Root();
        List<string> findings = [];

        foreach (string module in ModuleNames)
        {
            foreach (string file in RepositoryPaths.SourceFiles(module, "*.cs").Where(IsPresentationCode))
            {
                foreach (int line in FixedTextsInCode(File.ReadAllLines(file)))
                {
                    findings.Add($"{Path.GetRelativePath(root, file)}:{line}");
                }
            }
        }

        Assert.True(
            findings.Count == 0,
            "Fester Anzeigetext im Code statt UiText:" + Environment.NewLine + string.Join(Environment.NewLine, findings));
    }

    [Fact]
    public void NoDocumentTemplateCarriesAFixedDisplayText()
    {
        string root = RepositoryPaths.Root();
        List<string> findings = [];

        foreach (string file in RepositoryPaths.SourceFiles("MdExplorer.App", "*.html"))
        {
            string markup = ScriptOrStyle().Replace(File.ReadAllText(file), string.Empty);
            foreach (Match text in HtmlText().Matches(markup))
            {
                if (Word().IsMatch(Placeholder().Replace(text.Groups["value"].Value, string.Empty)))
                {
                    findings.Add($"{Path.GetRelativePath(root, file)}: {text.Groups["value"].Value.Trim()}");
                }
            }
        }

        Assert.True(
            findings.Count == 0,
            "Fester Anzeigetext in einer Dokumentvorlage statt Platzhalter:" + Environment.NewLine + string.Join(Environment.NewLine, findings));
    }

    // Die Gegenprobe auf festen Schnipseln: Ohne sie stünde der Wächter auch dann grün, wenn die
    // Erkennung gar nichts mehr träfe.
    [Theory]
    [InlineData("<Button Content=\"Anwenden\" IsDefault=\"True\" />", 1)]
    [InlineData("<MenuItem Header=\"Datei\">", 1)]
    [InlineData("<Button Title=\"{l:Loc A_B}\" ToolTip=\"Hilfe\" />", 1)]
    [InlineData("<Hyperlink NavigateUri=\"{Binding Url}\">Release öffnen</Hyperlink>", 1)]
    [InlineData("<TextBlock Text=\"{l:Loc Settings_Title}\" />", 0)]
    [InlineData("<Run Text=\"{Binding Count, Mode=OneWay}\" />", 0)]
    [InlineData("<Button Content=\"✕\" />", 0)]
    [InlineData("<Button Content=\"x\" />", 0)]
    [InlineData("<TextBlock Text=\"MdExplorer\" />", 0)]
    [InlineData("<ContentPresenter HorizontalContentAlignment=\"Center\" />", 0)]
    [InlineData("<local:EmptyState Headline=\"Nichts gefunden\" />", 1)]
    [InlineData("<MenuItem InputGestureText=\"Strg+,\" />", 1)]
    [InlineData("<TextBlock Text=\"{Binding When, StringFormat='Letzter Lauf: {0:t}'}\" />", 1)]
    [InlineData("<Run Text=\"{Binding Name, StringFormat='{}{0} öffnen'}\" />", 1)]
    [InlineData("<TextBlock Text=\"{Binding When, TargetNullValue='—'}\" />", 0)]
    [InlineData("<TextBlock Text=\"{Binding Count, StringFormat='{}{0:N0}'}\" />", 0)]
    [InlineData("<TextBlock Text=\"{Binding Name, StringFormat={l:Loc Open_Format}}\" />", 0)]
    public void TheDetection_FindsExactlyTheFixedTexts(string markup, int expected)
    {
        ArgumentNullException.ThrowIfNull(markup);
        Assert.Equal(expected, FixedTexts(markup.Split('\n')).Count);
    }

    [Theory]
    [InlineData("        Label = \"Kein Treffer\";", 1)]
    [InlineData("        string text = $\"Treffer bei {index}\";", 1)]
    [InlineData("        Placeholder = \"Suchen …\";", 1)]
    [InlineData("        string key = \"Action_Close\";", 0)]
    [InlineData("        Text = UiText.Get(\"Document_NoHit\");", 0)]
    [InlineData("    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = \"Datei fehlt.\")]", 0)]
    [InlineData("        throw new InvalidOperationException(\"Eingebettete Ressource fehlt.\");", 0)]
    [InlineData("        // Ein Kommentar mit \"Anführung\" darin", 0)]
    [InlineData("        _ = await RunScriptAsync(\"window.MdExplorerHelp.next()\");", 0)]
    [InlineData("        Justification = \"Die feste Verdrahtung ist hier gewollt.\")]", 0)]
    [InlineData("        .Append(\"<!doctype html>\")", 0)]
    [InlineData("        \"S1075:URIs should not be hardcoded\",", 0)]
    public void TheCodeDetection_FindsExactlyTheFixedTexts(string code, int expected)
    {
        ArgumentNullException.ThrowIfNull(code);
        Assert.Equal(expected, FixedTextsInCode([code]).Count);
    }

    [Fact]
    public void TheCodeDetection_TreatsTheContinuationOfAnExceptionAsDeveloperText()
    {
        string[] code =
        [
            "            throw new InvalidOperationException(",
            "                $\"Eingebettete Ressource fehlt: {name}.\");",
            "            Label = \"Kein Treffer\";",
        ];

        Assert.Equal([3], FixedTextsInCode(code));
    }

    [Fact]
    public void TheKeySearch_FindsMarkupAndCodeUsages()
    {
        const string source = "Title=\"{l:Loc Graph_Title}\" UiText.Get(\"Action_Close\") UiText.Format(\n    \"Status_Loaded\", 3)";

        Assert.Equal(["Action_Close", "Graph_Title", "Status_Loaded"], UsedKeys(source).Order(StringComparer.Ordinal));
    }

    private static List<int> FixedTexts(string[] lines)
    {
        List<int> findings = [];
        for (int i = 0; i < lines.Length; i++)
        {
            bool fixedText = DisplayAttribute().Matches(lines[i]).Any(match => IsFixedText(match.Groups["value"].Value))
                || ElementText().Matches(lines[i]).Any(match => IsFixedText(match.Groups["value"].Value))
                || BindingText().Matches(lines[i]).Any(match => IsFixedFormat(match.Groups["value"].Value));
            if (fixedText)
            {
                findings.Add(i + 1);
            }
        }

        return findings;
    }

    // Bindungen und Markup-Erweiterungen beginnen mit „{". Symbole wie ✕ oder ein einzelnes x
    // auf einer Schaltfläche sind kein Wort und brauchen keine Übersetzung.
    private static bool IsFixedText(string value) =>
        !value.StartsWith('{')
        && Word().IsMatch(value)
        && !BrandNames.Contains(value.Trim(), StringComparer.Ordinal);

    // In einem Format zählt nur, was um die Platzhalter herum steht: „{}{0:N0}" ist kein Text,
    // „{}{0} öffnen" schon.
    private static bool IsFixedFormat(string value) =>
        !DateMask().IsMatch(value) && Word().IsMatch(FormatItem().Replace(value, string.Empty));

    // Was im Code nach Anzeigetext aussieht: mehrere Wörter oder ein Umlaut. Schlüssel, Bezeichner
    // und Skriptaufrufe sind einzelne Wörter ohne Leerzeichen. Protokoll, Ausnahmen und
    // Kommentare richten sich an Entwickler und bleiben stehen.
    private static List<int> FixedTextsInCode(string[] lines)
    {
        List<int> findings = [];
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimStart();
            if (DeveloperFacing().IsMatch(line) || ContinuesAnException(lines, i))
            {
                continue;
            }
            if (StringLiteral().Matches(line).Any(match => LooksLikeProse(match.Groups["value"].Value)))
            {
                findings.Add(i + 1);
            }
        }

        return findings;
    }

    // Eine lange Ausnahmemeldung steht oft auf der Zeile nach „throw new …Exception(“.
    private static bool ContinuesAnException(string[] lines, int index) =>
        index > 0 && lines[index - 1].TrimEnd().EndsWith("Exception(", StringComparison.Ordinal);

    // Ein Literal, das mit „<“ beginnt, ist HTML-Gerüst; Text in Dokumentvorlagen prüft der
    // Wächter für die Vorlagen eigens. „S1075:…“ ist die Kennung einer Analyseregel.
    private static bool LooksLikeProse(string value)
    {
        if (value.StartsWith('<') || AnalyzerRule().IsMatch(value))
        {
            return false;
        }

        string text = FormatItem().Replace(value, string.Empty);
        return Word().IsMatch(text) && (SeveralWords().IsMatch(text) || text.IndexOfAny(['ä', 'ö', 'ü', 'Ä', 'Ö', 'Ü', 'ß', '…']) >= 0);
    }

    // Code hinter den Ansichten, Bausteine, Wandler und Dienste; Dienste, weil ihre Meldungen in
    // Statuszeile und Rückfragen landen. ViewModels prüft dieser Wächter nicht: Dort stehen Texte
    // über UiText, und die Schlüsselprüfung oben hält sie fest.
    private static bool IsPresentationCode(string file)
    {
        string[] parts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Contains("Views") || parts.Contains("Controls") || parts.Contains("Converters")
            || parts.Contains("Services") || Path.GetFileName(file) == "App.xaml.cs";
    }

    private static IEnumerable<string> UsedKeys(string source) =>
        KeyUsage().Matches(source).Select(match => match.Groups["key"].Value);

    private static List<KeyValuePair<string, string>> Entries(string module)
    {
        string path = Path.Combine(RepositoryPaths.Project(module), "Strings", "Resources.resx");

        return
        [
            .. XDocument.Load(path).Root!.Elements("data").Select(data =>
                new KeyValuePair<string, string>((string)data.Attribute("name")!, (string?)data.Element("value") ?? string.Empty)),
        ];
    }

    // Bereich, Unterstrich, Fall: „Settings_Title", „Update_Status_Checking".
    [GeneratedRegex(@"^[A-Z][A-Za-z]*(_[A-Z][A-Za-z0-9]*)+$")]
    private static partial Regex KeyPattern();

    // Die Attribute, deren Wert in der Oberfläche zu lesen ist. Der Lookbehind verhindert, dass
    // „HorizontalContentAlignment" als „Content" zählt.
    [GeneratedRegex(@"(?<![\w.:])(Text|Header|Content|Title|Label|Placeholder|ResetLabel|Headline|Hint|InputGestureText|ToolTip|AutomationProperties\.Name|AutomationProperties\.HelpText)\s*=\s*""(?<value>[^""]*)""")]
    private static partial Regex DisplayAttribute();

    // Texte, die eine Bindung selbst mitbringt.
    [GeneratedRegex(@"(StringFormat|TargetNullValue|FallbackValue)\s*=\s*'(?<value>[^']*)'")]
    private static partial Regex BindingText();

    // Eine reine Datumsmaske wie „yyyy-MM-dd HH:mm:ss.fff" ist Format, kein Text.
    [GeneratedRegex(@"^[yMdHhmsfFtz.:/\- ]+$")]
    private static partial Regex DateMask();

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex FormatItem();

    [GeneratedRegex(@"""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"^(//|\[LoggerMessage|\[GeneratedRegex|throw |Log\.|Justification\s*=|.*Exception\()")]
    private static partial Regex DeveloperFacing();

    [GeneratedRegex(@"^[A-Z]{1,4}\d{3,5}:")]
    private static partial Regex AnalyzerRule();

    [GeneratedRegex(@"\p{L}{2,}\s+\p{L}")]
    private static partial Regex SeveralWords();

    [GeneratedRegex(@"<(script|style)\b.*?</\1>", RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@">(?<value>[^<>]+)<")]
    private static partial Regex HtmlText();

    [GeneratedRegex(@"__[A-Z_]+__")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@">(?<value>[^<>]+)</(TextBlock|Run|Button|Label|CheckBox|RadioButton|MenuItem|TabItem|ComboBoxItem|Hyperlink|Span|Bold|Italic)>")]
    private static partial Regex ElementText();

    [GeneratedRegex(@"\p{L}{2,}")]
    private static partial Regex Word();

    [GeneratedRegex(@"(\{l:Loc\s+|UiText\.(Get|Format)\(\s*"")(?<key>\w+)")]
    private static partial Regex KeyUsage();
}
