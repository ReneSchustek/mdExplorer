using MdExplorer.Core.Diagnostics;

namespace MdExplorer.Core.Tests.Diagnostics;

/// <summary>Tests für den Betriebs-Stand der nicht verarbeitbaren Dateien.</summary>
public sealed class ParseFailureStatusTests
{
    [Fact]
    public void NewInstance_StartsAtZero()
    {
        ParseFailureStatus sut = new();

        Assert.Equal(0, sut.UnparsableFileCount);
    }

    [Fact]
    public void Update_OnNewCount_FiresChanged()
    {
        ParseFailureStatus sut = new();
        int changedCount = 0;
        sut.Changed += (_, _) => changedCount++;

        sut.Update(3);

        Assert.Equal(3, sut.UnparsableFileCount);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void Update_OnSameCount_DoesNotFireAgain()
    {
        ParseFailureStatus sut = new();
        sut.Update(2);
        int changedCount = 0;
        sut.Changed += (_, _) => changedCount++;

        sut.Update(2);

        Assert.Equal(0, changedCount);
    }

    [Fact]
    public void Update_OnNegativeCount_Throws()
    {
        ParseFailureStatus sut = new();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => sut.Update(-1));
    }

    [Fact]
    public void Update_BackToZero_FiresChanged()
    {
        ParseFailureStatus sut = new();
        sut.Update(1);
        int changedCount = 0;
        sut.Changed += (_, _) => changedCount++;

        sut.Update(0);

        Assert.Equal(0, sut.UnparsableFileCount);
        Assert.Equal(1, changedCount);
    }
}
