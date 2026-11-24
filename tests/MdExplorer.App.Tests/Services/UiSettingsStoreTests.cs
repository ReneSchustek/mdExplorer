using System.Text;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Spaltenbreiten überstehen einen Neustart, und ohne gespeicherte Datei öffnet das Fenster mit
/// <see cref="UiLayout.Default"/> statt mit einem Fehler. Geschrieben wird nur über den atomaren
/// Weg des Dateisystems.
/// </summary>
public sealed class UiSettingsStoreTests
{
    private const string FilePath = @"C:\Anwendungsdaten\MdExplorer\" + UiSettingsStore.LayoutFileName;

    private readonly FakeFileSystem _fileSystem = new();

    [Fact]
    public void SaveAndLoad_WithAPlainLayout_RoundTripsIt()
    {
        UiSettingsStore store = CreateStore();
        UiLayout expected = new(280, 580);

        store.Save(expected);
        UiLayout actual = store.Load();

        Assert.Equal(expected, actual);
    }

    /// <remarks>
    /// Ein Absturz beim Schließen darf keine halbe Datei hinterlassen; sonst wären Spaltenbreiten,
    /// Hilfefenster und Graph-Filter beim nächsten Start verloren. Der atomare Weg des
    /// Dateisystems schreibt erst eine Zwischendatei und benennt sie dann um.
    /// </remarks>
    [Fact]
    public void Save_OnAnyLayout_WritesOnlyThroughTheAtomicPath()
    {
        UiSettingsStore store = CreateStore();

        store.Save(new UiLayout(300, 500));

        Assert.Equal([FilePath], _fileSystem.WrittenFiles.Keys);
        Assert.Contains("\"FolderColumnWidth\": 300", Encoding.UTF8.GetString(_fileSystem.WrittenFiles[FilePath]), StringComparison.Ordinal);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefault()
    {
        UiSettingsStore store = CreateStore();

        UiLayout layout = store.Load();

        Assert.Equal(UiLayout.Default, layout);
    }

    /// <remarks>
    /// Die Datei liegt im Profil des Nutzers und lässt sich von Hand bearbeiten. Eine negative Breite
    /// ließ das Hauptfenster beim Aufbau werfen, und zwar bei jedem Start; jetzt gilt dafür der Standard.
    /// </remarks>
    [Fact]
    public void Load_WithValuesTheWindowCannotUse_FallsBackForEachOfThem()
    {
        _fileSystem.Files[FilePath] = Encoding.UTF8.GetBytes("""
            { "FolderColumnWidth": -40, "PreviewColumnWidth": 0, "LeftTabIndex": 7,
              "HelpWindow": { "Left": 10, "Top": 10, "Width": -1, "Height": 300 },
              "RecentFiles": [ "notiz.md", null, "" ] }
            """);
        UiSettingsStore store = CreateStore();

        UiLayout layout = store.Load();

        Assert.Equal(UiLayout.Default.FolderColumnWidth, layout.FolderColumnWidth);
        Assert.Equal(UiLayout.Default.PreviewColumnWidth, layout.PreviewColumnWidth);
        Assert.Equal(0, layout.LeftTabIndex);
        Assert.Null(layout.HelpWindow);
        Assert.Equal(["notiz.md"], layout.RecentFiles!);
    }

    [Fact]
    public void Load_WithUsableValues_KeepsThem()
    {
        UiSettingsStore store = CreateStore();
        UiLayout saved = new(280, 580, new WindowGeometry(10, 20, 800, 600), IsTagCloudVisible: false, LeftTabIndex: 2);
        store.Save(saved);

        Assert.Equal(saved, store.Load());
        Assert.DoesNotContain("IsUsable", Encoding.UTF8.GetString(_fileSystem.WrittenFiles[FilePath]), StringComparison.Ordinal);
    }

    private UiSettingsStore CreateStore() => new(_fileSystem, FilePath, NullLogger<UiSettingsStore>.Instance);
}
