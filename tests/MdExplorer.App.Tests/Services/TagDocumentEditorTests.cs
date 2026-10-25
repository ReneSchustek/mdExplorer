using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Schlagwörter ändern nur die Hashtags und den verwalteten Block am Dateiende, der übrige Text
/// bleibt unberührt. Entfernen trifft nur das ganze Wort: <c>#tag</c> nimmt <c>#tagging</c>
/// nicht mit.
/// </summary>
public sealed class TagDocumentEditorTests
{
    [Fact]
    public void Add_WithoutExistingBlock_AppendsManagedCommentAtEnd()
    {
        string source = "# Titel\n\nText";

        string result = TagDocumentEditor.Add(source, "neu", []);

        Assert.Contains("<!-- mdexplorer-tags: #neu -->", result, StringComparison.Ordinal);
        Assert.StartsWith("# Titel", result, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Mit Leerzeichen zerrisse der Name im Text den Hashtag, mit <c>&gt;</c> oder <c>--&gt;</c>
    /// endete der verwaltete Kommentar zu früh, und sein Rest stünde sichtbar in der Vorschau.
    /// Solche Namen sind ein Fehler des Aufrufers; die Oberfläche lässt sie gar nicht erst zu.
    /// </remarks>
    [Theory]
    [InlineData("zwei worte")]
    [InlineData("a-->b")]
    [InlineData("a>b")]
    [InlineData("komma,dahinter")]
    public void AddAndRename_WithAnInvalidName_Throw(string name)
    {
        _ = Assert.Throws<ArgumentException>(() => TagDocumentEditor.Add("Text", name, []));
        _ = Assert.Throws<ArgumentException>(() => TagDocumentEditor.Rename("Text mit #alt", "alt", name));
    }

    /// <remarks>
    /// Eine Datei mit Windows-Zeilenenden bleibt dabei. Ein angehängter Block mit bloßem LF ergäbe
    /// gemischte Zeilenenden, und manche Werkzeuge melden die Datei danach als ganz geändert.
    /// </remarks>
    [Theory]
    [InlineData("# Titel\r\n\r\nText\r\n", "# Titel\r\n\r\nText\r\n<!-- mdexplorer-tags: #neu -->\r\n")]
    [InlineData("# Titel\r\n\r\nText", "# Titel\r\n\r\nText\r\n\r\n<!-- mdexplorer-tags: #neu -->\r\n")]
    [InlineData("Text\n", "Text\n<!-- mdexplorer-tags: #neu -->\n")]
    public void Add_KeepsTheLineEndingsOfTheDocument(string source, string expected)
    {
        Assert.Equal(expected, TagDocumentEditor.Add(source, "neu", []));
    }

    [Fact]
    public void Add_WithExistingBlock_AppendsTagToBlock()
    {
        string source = "Text\n<!-- mdexplorer-tags: #alt -->\n";

        string result = TagDocumentEditor.Add(source, "neu", ["alt"]);

        Assert.Contains("<!-- mdexplorer-tags: #alt #neu -->", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_AlreadyPresentInBody_LeavesDocumentUnchanged()
    {
        string source = "Text mit #vorhanden inline.";

        string result = TagDocumentEditor.Add(source, "vorhanden", ["vorhanden"]);

        Assert.Equal(source, result);
    }

    [Fact]
    public void Remove_WithDifferentCase_RemovesAllOccurrences()
    {
        string source = "Eins #Tag zwei #tag drei";

        string result = TagDocumentEditor.Remove(source, "tag");

        Assert.DoesNotContain("#Tag", result, StringComparison.Ordinal);
        Assert.DoesNotContain("#tag", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_EmptyManagedBlock_IsRemoved()
    {
        string source = "Body\n<!-- mdexplorer-tags: #weg -->\n";

        string result = TagDocumentEditor.Remove(source, "weg");

        Assert.DoesNotContain("mdexplorer-tags", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_PartialPrefixMatch_IsPreserved()
    {
        string source = "#tagging soll bleiben";

        string result = TagDocumentEditor.Remove(source, "tag");

        Assert.Contains("#tagging", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Rename_WithSeveralOccurrences_ReplacesAll()
    {
        string source = "Eins #alt und #alt nochmal.";

        string result = TagDocumentEditor.Rename(source, "alt", "neu");

        Assert.DoesNotContain("#alt", result, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(result, "#neu"));
    }

    [Fact]
    public void Rename_SameNames_NoChange()
    {
        string source = "Text #foo";

        string result = TagDocumentEditor.Rename(source, "foo", "foo");

        Assert.Equal(source, result);
    }

    private static int CountOccurrences(string text, string substring)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }
}
