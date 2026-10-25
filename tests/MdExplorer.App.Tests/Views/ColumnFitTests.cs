using MdExplorer.App.Views;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Wird das Fenster zu schmal, geben zuerst die linke Spalte und dann die Tag-Cloud nach, bis zu
/// ihren Mindestbreiten. Bei genug Platz bleiben beide bei ihrer Wunschbreite.
/// </summary>
public sealed class ColumnFitTests
{
    // Trenner (6 + 6) und Mindestbreite der Vorschau (320).
    private const double Reserved = 332.0;

    private static readonly ColumnWish Folder = new(Wanted: 320.0, Minimum: 240.0);
    private static readonly ColumnWish TagCloud = new(Wanted: 260.0, Minimum: 180.0);

    [Fact]
    public void Fit_WithEnoughRoom_KeepsTheWantedWidths()
    {
        ColumnWidths widths = ColumnFit.Fit(1280.0, Reserved, Folder, TagCloud);

        Assert.Equal(new ColumnWidths(320.0, 260.0), widths);
    }

    [Fact]
    public void Fit_WhenTight_ShrinksTheFolderColumnFirst()
    {
        // Platz für Spalten: 884 - 332 = 552; die Tag-Cloud behält 260, links bleiben 292.
        ColumnWidths widths = ColumnFit.Fit(884.0, Reserved, Folder, TagCloud);

        Assert.Equal(new ColumnWidths(292.0, 260.0), widths);
    }

    [Fact]
    public void Fit_WhenVeryTight_ShrinksTheTagCloudAfterTheFolderReachedItsMinimum()
    {
        // 772 - 332 = 440: links 240 (Minimum), für die Tag-Cloud bleiben 200.
        ColumnWidths widths = ColumnFit.Fit(772.0, Reserved, Folder, TagCloud);

        Assert.Equal(new ColumnWidths(240.0, 200.0), widths);
    }

    [Fact]
    public void Fit_BelowAllMinimums_StopsAtTheMinimums()
    {
        ColumnWidths widths = ColumnFit.Fit(500.0, Reserved, Folder, TagCloud);

        Assert.Equal(new ColumnWidths(240.0, 180.0), widths);
    }

    [Fact]
    public void Fit_WithTheTagCloudHidden_GivesTheFolderColumnTheRoom()
    {
        ColumnWidths widths = ColumnFit.Fit(700.0, Reserved - 6.0, Folder, new ColumnWish(0.0, TagCloud.Minimum));

        Assert.Equal(new ColumnWidths(320.0, 0.0), widths);
    }

    [Fact]
    public void Fit_BeforeTheFirstLayout_StaysAtTheMinimums()
    {
        // Vor dem ersten Durchlauf ist die Breite 0; daraus darf keine negative Breite werden.
        ColumnWidths widths = ColumnFit.Fit(0.0, Reserved, Folder, TagCloud);

        Assert.Equal(new ColumnWidths(240.0, 180.0), widths);
    }

    [Fact]
    public void Fit_RejectsMissingWishes()
    {
        _ = Assert.Throws<ArgumentNullException>(() => ColumnFit.Fit(1000.0, Reserved, null!, TagCloud));
        _ = Assert.Throws<ArgumentNullException>(() => ColumnFit.Fit(1000.0, Reserved, Folder, null!));
    }
}
