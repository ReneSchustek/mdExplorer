using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Services;

/// <summary>Tests des reinen Find-Algorithmus fuer den Editor (Ctrl+F).</summary>
public sealed class EditorFindHelperTests
{
    [Fact]
    public void FindNext_OnMatch_ReturnsFirstHitAfterCursor()
    {
        const string text = "Lorem ipsum dolor sit amet, lorem ipsum.";

        EditorFindHelper.FindMatch? match = EditorFindHelper.FindNext(text, "ipsum", startIndex: 0);

        Assert.True(match.HasValue);
        Assert.Equal(6, match!.Value.StartIndex);
        Assert.Equal(5, match.Value.Length);
    }

    [Fact]
    public void FindNext_AfterFirstHit_FindsSecondOccurrence()
    {
        const string text = "Lorem ipsum dolor sit amet, lorem ipsum.";

        EditorFindHelper.FindMatch? match = EditorFindHelper.FindNext(text, "ipsum", startIndex: 11);

        Assert.True(match.HasValue);
        Assert.Equal(34, match!.Value.StartIndex);
    }

    [Fact]
    public void FindNext_PastLastMatch_WrapsToStart()
    {
        const string text = "Lorem ipsum dolor.";

        EditorFindHelper.FindMatch? match = EditorFindHelper.FindNext(text, "ipsum", startIndex: text.Length);

        Assert.True(match.HasValue);
        Assert.Equal(6, match!.Value.StartIndex);
    }

    [Fact]
    public void FindNext_CaseSensitiveMissesLowercase_ReturnsNull()
    {
        const string text = "Lorem IPSUM dolor.";

        EditorFindHelper.FindMatch? match = EditorFindHelper.FindNext(text, "ipsum", startIndex: 0, caseSensitive: true);

        Assert.False(match.HasValue);
    }

    [Fact]
    public void FindNext_OnEmptyQuery_ReturnsNull()
    {
        EditorFindHelper.FindMatch? match = EditorFindHelper.FindNext("abc", string.Empty, startIndex: 0);

        Assert.False(match.HasValue);
    }

    [Fact]
    public void FindNext_OnNoHit_ReturnsNull()
    {
        EditorFindHelper.FindMatch? match = EditorFindHelper.FindNext("Lorem ipsum.", "missing", startIndex: 0);

        Assert.False(match.HasValue);
    }

    [Fact]
    public void FindPrevious_BeforeCursor_ReturnsLastHitInRange()
    {
        const string text = "Lorem ipsum dolor sit amet, lorem ipsum.";

        EditorFindHelper.FindMatch? match = EditorFindHelper.FindPrevious(text, "ipsum", endIndex: 30);

        Assert.True(match.HasValue);
        // Der Treffer bei Index 6 ist der einzige strikt VOR endIndex=30 (zweiter beginnt bei 34).
        Assert.Equal(6, match!.Value.StartIndex);
    }

    [Fact]
    public void FindPrevious_AtStart_WrapsToEnd()
    {
        const string text = "Lorem ipsum dolor sit amet, lorem ipsum.";

        EditorFindHelper.FindMatch? match = EditorFindHelper.FindPrevious(text, "ipsum", endIndex: 0);

        Assert.True(match.HasValue);
        Assert.Equal(34, match!.Value.StartIndex);
    }

    [Fact]
    public void FindPrevious_OnEmptyQuery_ReturnsNull()
    {
        EditorFindHelper.FindMatch? match = EditorFindHelper.FindPrevious("abc", string.Empty, endIndex: 3);

        Assert.False(match.HasValue);
    }
}
