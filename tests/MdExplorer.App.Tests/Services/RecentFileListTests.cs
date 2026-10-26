using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Der Verlauf: jüngste Datei zuerst, jede nur einmal, höchstens <see cref="RecentFileList.Capacity"/>.
/// </summary>
public sealed class RecentFileListTests
{
    [Fact]
    public void Add_PutsTheNewestFirst()
    {
        RecentFileList sut = new(paths: null);

        Assert.True(sut.Add(@"C:\a.md"));
        Assert.True(sut.Add(@"C:\b.md"));

        Assert.Equal([@"C:\b.md", @"C:\a.md"], sut.Paths);
    }

    [Fact]
    public void Add_AnExistingFile_MovesItToTheFrontWithoutDuplicate()
    {
        RecentFileList sut = new([@"C:\b.md", @"C:\a.md"]);

        Assert.True(sut.Add(@"C:\a.md"));

        Assert.Equal([@"C:\a.md", @"C:\b.md"], sut.Paths);
    }

    [Fact]
    public void Add_TheSameFileInAnotherCase_KeepsOneEntryInTheNewSpelling()
    {
        RecentFileList sut = new([@"C:\Notizen\Plan.md"]);

        Assert.True(sut.Add(@"C:\notizen\plan.md"));

        Assert.Equal([@"C:\notizen\plan.md"], sut.Paths);
    }

    [Fact]
    public void Add_TheFirstEntryAgain_ChangesNothing()
    {
        RecentFileList sut = new([@"C:\a.md"]);

        Assert.False(sut.Add(@"C:\a.md"));
    }

    [Fact]
    public void Add_BeyondTheCapacity_DropsTheOldest()
    {
        RecentFileList sut = new(paths: null);
        for (int i = 0; i <= RecentFileList.Capacity; i++)
        {
            _ = sut.Add($@"C:\{i}.md");
        }

        Assert.Equal(RecentFileList.Capacity, sut.Paths.Count);
        Assert.Equal($@"C:\{RecentFileList.Capacity}.md", sut.Paths[0]);
        Assert.DoesNotContain(@"C:\0.md", sut.Paths);
    }

    [Fact]
    public void Constructor_SkipsBlanksDuplicatesAndTheOverflow()
    {
        string[] stored = [@"C:\a.md", " ", @"C:\A.md", @"C:\b.md", .. Enumerable.Range(0, 20).Select(i => $@"C:\x{i}.md")];

        RecentFileList sut = new(stored);

        Assert.Equal(RecentFileList.Capacity, sut.Paths.Count);
        Assert.Equal([@"C:\a.md", @"C:\b.md"], sut.Paths.Take(2));
    }

    [Fact]
    public void Remove_TakesTheEntryOut()
    {
        RecentFileList sut = new([@"C:\a.md", @"C:\b.md"]);

        Assert.True(sut.Remove(@"C:\A.md"));
        Assert.False(sut.Remove(@"C:\a.md"));

        Assert.Equal([@"C:\b.md"], sut.Paths);
    }

    [Fact]
    public void Clear_EmptiesTheList_AndReportsWhetherThereWasSomething()
    {
        RecentFileList sut = new([@"C:\a.md"]);

        Assert.True(sut.Clear());
        Assert.False(sut.Clear());
        Assert.Empty(sut.Paths);
    }

    [Fact]
    public void Add_WithoutPath_Throws()
    {
        _ = Assert.Throws<ArgumentException>(() => new RecentFileList(paths: null).Add(" "));
    }
}
