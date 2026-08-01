using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Services;

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
    public void Remove_RemovesAllOccurrencesIgnoringCase()
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
    public void Rename_ReplacesAllOccurrences()
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
