using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Die Wurzel eines Eintrags ergibt sich aus seinen beiden Pfaden, aber nur an einer Ordnergrenze.
/// </summary>
public sealed class IndexRootPathTests
{
    [Theory]
    [InlineData(@"C:\notes\unter\a.md", "unter/a.md", "C:/notes/")]
    [InlineData(@"C:\notes\a.md", "a.md", "C:/notes/")]
    [InlineData(@"C:\notes\unter\a.md", @"unter\a.md", "C:/notes/")]
    [InlineData(@"C:\NOTES\Unter\A.md", "unter/a.md", "C:/NOTES/")]
    public void Of_WhenTheRelativePathEndsTheAbsoluteOne_ReturnsTheRoot(string absolutePath, string relativePath, string expected)
    {
        Assert.Equal(expected, IndexRootPath.Of(absolutePath, relativePath));
    }

    /// <remarks>
    /// „xunter/a.md“ endet zwar auf „unter/a.md“, aber mitten in einem Ordnernamen. Eine Wurzel
    /// „C:\notes\x“ gibt es nicht; wer mit ihr rechnete, legte Einträge unter falschen Pfaden ab.
    /// </remarks>
    [Theory]
    [InlineData(@"C:\notes\xunter\a.md", "unter/a.md")]
    [InlineData(@"C:\notes\a.md", "anders/a.md")]
    [InlineData(@"C:\notes\a.md", "")]
    public void Of_WhenTheRelativePathDoesNotEndAtAFolderBoundary_ReturnsNothing(string absolutePath, string relativePath)
    {
        Assert.Null(IndexRootPath.Of(absolutePath, relativePath));
    }
}
