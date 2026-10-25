using MdExplorer.App.Services;
using MdExplorer.App.Services.Help;
using MdExplorer.App.Tests.Fakes;

namespace MdExplorer.App.Tests.Services.Help;

/// <summary>
/// Das Hilfefenster liest und schreibt nur seine eigene Geometrie; die übrigen Felder des Layouts
/// bleiben, wie sie sind.
/// </summary>
public sealed class HelpWindowGeometryStoreTests
{
    [Fact]
    public void Load_WithoutSavedGeometry_ReturnsNothing()
    {
        HelpWindowGeometryStore sut = new(new FakeUiLayoutStore());

        Assert.Null(sut.Load());
    }

    /// <remarks>
    /// Hauptfenster, Hilfe und Graph schreiben in dieselbe Datei. Ein Hilfefenster, das ein neues
    /// Layout nur mit seiner Geometrie anlegte, setzte Spaltenbreiten und Graph-Filter zurück.
    /// </remarks>
    [Fact]
    public void Save_WithAnExistingLayout_KeepsItsOtherFields()
    {
        FakeUiLayoutStore layout = new() { Layout = new UiLayout(300, 500, LeftTabIndex: 2, GraphPathPrefix: "projekt") };
        HelpWindowGeometryStore sut = new(layout);
        WindowGeometry geometry = new(10, 20, 800, 600);

        sut.Save(geometry);

        Assert.Equal(new UiLayout(300, 500, geometry, LeftTabIndex: 2, GraphPathPrefix: "projekt"), layout.Layout);
        Assert.Equal(geometry, sut.Load());
    }

    [Fact]
    public void ConstructorAndSave_WithoutArgument_Throw()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new HelpWindowGeometryStore(null!));
        _ = Assert.Throws<ArgumentNullException>(() => new HelpWindowGeometryStore(new FakeUiLayoutStore()).Save(null!));
    }
}
