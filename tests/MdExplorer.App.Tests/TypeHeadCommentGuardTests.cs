using System.IO;
using System.Text.RegularExpressions;

namespace MdExplorer.App.Tests;

/// <summary>
/// Jeder Typ in <c>src</c> und <c>tests</c> sagt in einem Kopf-Kommentar, wofür es ihn gibt.
/// </summary>
/// <remarks>
/// <para>
/// Für öffentliche Typen erzwingt das schon der Übersetzer über CS1591. Interne und private Typen
/// fallen dort heraus, und gerade Projektionen, Attrappen und Testklassen sind fast alle intern
/// oder privat. Bei einer Testklasse zählt der Kopf am meisten: Der Methodenname sagt, was geprüft
/// wird, aber nicht, welchen Fehler der Test verhindert.
/// </para>
/// <para>
/// Ausgenommen ist nur, was ein Werkzeug erzeugt (EF-Migrationen, <c>*.g.cs</c>,
/// <c>*.Designer.cs</c>): Ein Kommentar dort ginge beim nächsten Erzeugen verloren.
/// </para>
/// </remarks>
public sealed partial class TypeHeadCommentGuardTests
{
    private static readonly string[] Folders = ["src", "tests"];

    [Fact]
    public void EveryTypeCarriesAHeadComment()
    {
        string root = RepositoryPaths.Root();
        List<string> findings = [];

        foreach (string file in SourceFiles(root))
        {
            foreach (int line in TypesWithoutHead(File.ReadAllLines(file)))
            {
                findings.Add($"{Path.GetRelativePath(root, file)}:{line}");
            }
        }

        Assert.True(
            findings.Count == 0,
            "Diese Typen stehen ohne Kopf-Kommentar da:" + Environment.NewLine + string.Join(Environment.NewLine, findings));
    }

    // Die Gegenprobe auf festen Schnipseln: Ohne sie stünde der Wächter auch dann grün, wenn die
    // Erkennung gar keine Deklaration mehr träfe.
    [Theory]
    [InlineData("public sealed class Without\n{\n}", 1)]
    [InlineData("using X;\n\n[Obsolete]\ninternal record Without(int A);", 1)]
    [InlineData("    private readonly record struct Without(int A);", 1)]
    [InlineData("// Kopf.\n\npublic sealed class SeparatedByBlankLine { }", 1)]
    [InlineData("// ------------\npublic sealed class SeparatorOnly { }", 1)]
    [InlineData("// var x = 1;\npublic sealed class CommentedOutCodeOnly { }", 1)]
    [InlineData("//TODO\npublic sealed class TodoOnly { }", 1)]
    [InlineData("// Kopf.\npublic sealed class With\n{\n}", 0)]
    [InlineData("// Kopf.\n[Obsolete]\n[Serializable]\npublic enum With { A }", 0)]
    [InlineData("    /// <summary>Kopf.</summary>\n    private sealed class With { }", 0)]
    [InlineData("/// <summary>\n/// Kopf.\n/// </summary>\n/// <param name=\"a\">A.</param>\ninternal sealed record With(int A);", 0)]
    [InlineData("// Kopf.\n[SuppressMessage(\"A\", \"B\",\n    Justification = \"C\")]\npublic sealed class WithMultiLineAttribute { }", 0)]
    [InlineData("[SuppressMessage(\"A\", \"B\",\n    Justification = \"C\")]\npublic sealed class WithoutBehindMultiLineAttribute { }", 1)]
    [InlineData("var text = \"class None\";", 0)]
    public void TheDetection_FindsExactlyTheTypesWithoutHead(string source, int expected)
    {
        ArgumentNullException.ThrowIfNull(source);
        Assert.Equal(expected, TypesWithoutHead(source.Split('\n')).Count);
    }

    // Über Attribute hinweg wird die nächste Zeile darüber gelesen. Eine Leerzeile beendet die
    // Suche: Ein Kommentar, der abgesetzt darüber steht, gehört zu etwas anderem.
    private static List<int> TypesWithoutHead(string[] lines)
    {
        List<int> missing = [];
        for (int i = 0; i < lines.Length; i++)
        {
            if (!TypeDeclaration().IsMatch(lines[i]))
            {
                continue;
            }

            if (!HeadEndsAt(lines, AboveAttributes(lines, i - 1)))
            {
                missing.Add(i + 1);
            }
        }

        return missing;
    }

    // Ein Attribut kann über mehrere Zeilen laufen (SuppressMessage mit Justification). Endet eine
    // Zeile auf „]", wird bis zur Zeile mit der öffnenden Klammer zurückgegangen; sonst stünde die
    // Fortsetzungszeile zwischen Kopf und Typ und der Kopf zählte nicht.
    private static int AboveAttributes(string[] lines, int line)
    {
        while (line >= 0)
        {
            string text = lines[line].Trim();
            if (text.StartsWith("//", StringComparison.Ordinal))
            {
                break;
            }

            if (text.StartsWith('['))
            {
                line--;
                continue;
            }

            if (!text.EndsWith(']'))
            {
                break;
            }

            int opening = line;
            while (opening >= 0 && !lines[opening].TrimStart().StartsWith('['))
            {
                opening--;
            }

            if (opening < 0)
            {
                break;
            }

            line = opening - 1;
        }

        return line;
    }

    // Der Kommentarblock über der Deklaration muss wenigstens eine Zeile mit Inhalt haben. Eine
    // Trennlinie, auskommentierter Code oder ein Merkposten ohne Text sagen nicht, wofür es den
    // Typ gibt.
    private static bool HeadEndsAt(string[] lines, int last)
    {
        bool prose = false;
        int line = last;
        while (line >= 0 && lines[line].TrimStart().StartsWith("//", StringComparison.Ordinal))
        {
            string text = lines[line].Trim().TrimStart('/').Trim();
            if (text.Length > 0 && !SeparatorLine().IsMatch(text) && !CommentedOutCode().IsMatch(text)
                && !text.StartsWith("TODO", StringComparison.Ordinal))
            {
                prose = true;
            }

            line--;
        }

        return prose;
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Folders
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            .Where(file => !IsGenerated(file));

    private static bool IsGenerated(string file)
    {
        string[] parts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Contains("obj") || parts.Contains("bin") || parts.Contains("Migrations")
            || file.EndsWith(".g.cs", StringComparison.Ordinal)
            || file.EndsWith(".g.i.cs", StringComparison.Ordinal)
            || file.EndsWith(".Designer.cs", StringComparison.Ordinal);
    }

    // Eine Deklaration am Zeilenanfang mit beliebigen Modifizierern davor. Das Wort „class" in einer
    // Zeichenkette oder mitten in einer Zeile zählt nicht.
    [GeneratedRegex(@"^\s*((public|internal|private|protected|file|static|sealed|abstract|partial|readonly|ref)\s+)*(class|record|interface|enum|struct)\s+\w+")]
    private static partial Regex TypeDeclaration();

    // Eine Zeile nur aus Strichen, Gleichheitszeichen oder Sternen.
    [GeneratedRegex(@"^[-=*#~_]{3,}$")]
    private static partial Regex SeparatorLine();

    [GeneratedRegex(@"[;{}]\s*$|^(using|var|return|public|private|internal)\b")]
    private static partial Regex CommentedOutCode();
}
