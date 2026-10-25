using System.IO;
using System.Text;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Prüft, dass eine beschädigte oder unzugängliche Layout-Datei den Programmstart nicht
/// verhindert und ein Schreibfehler das Beenden nicht aufhält. Würde das Laden werfen, käme die
/// Anwendung nie bis zum Hauptfenster.
/// </summary>
/// <remarks>
/// Die Fehler kommen aus dem Dateisystem im Speicher. Welche Ausnahme Windows für eine gesperrte
/// oder verweigerte Datei wirft, belegen die Tests des echten Dateisystems; hier zählt, dass der
/// Speicher beide Arten auffängt.
/// </remarks>
public sealed class UiSettingsStoreRobustnessTests
{
    private const string FilePath = @"C:\Anwendungsdaten\MdExplorer\" + UiSettingsStore.LayoutFileName;

    private readonly FakeFileSystem _fileSystem = new();

    [Theory]
    [InlineData("{ das ist kein json")]
    // "null" ist gültiges JSON, ergibt aber kein Layout. Ohne die Absicherung bekäme der
    // Aufrufer null statt eines Layouts.
    [InlineData("null")]
    public void Load_OnUnusableContent_FallsBackToTheDefaultLayout(string content)
    {
        _fileSystem.Files[FilePath] = Encoding.UTF8.GetBytes(content);

        Assert.Equal(UiLayout.Default, CreateStore().Load());
    }

    /// <remarks>
    /// Eine zweite Programm-Instanz oder ein Virenscanner hält die Datei (E/A-Fehler), oder Rechte
    /// nach einer Wiederherstellung verweigern das Lesen (Zugriffsfehler). Der Start darf an
    /// beidem nicht scheitern.
    /// </remarks>
    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void Load_WhenReadingFails_FallsBackToTheDefaultLayout(Type failure)
    {
        _fileSystem.Files[FilePath] = Encoding.UTF8.GetBytes("{}");
        _fileSystem.FailOnRead = Assert.IsAssignableFrom<Exception>(Activator.CreateInstance(failure, "simuliert"));

        Assert.Equal(UiLayout.Default, CreateStore().Load());
    }

    /// <remarks>
    /// Gespeichert wird beim Schließen des Fensters. Ein Schreibfehler darf das Beenden nicht
    /// aufhalten, der Verlust der Spaltenbreiten ist verschmerzbar.
    /// </remarks>
    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void Save_WhenWritingFails_DoesNotThrow(Type failure)
    {
        _fileSystem.FailOnWrite = Assert.IsAssignableFrom<Exception>(Activator.CreateInstance(failure, "simuliert"));

        Exception? thrown = Record.Exception(() => CreateStore().Save(new UiLayout(300, 500)));

        Assert.Null(thrown);
        Assert.Empty(_fileSystem.WrittenFiles);
    }

    [Fact]
    public void SaveAndLoad_WithWindowGeometryAndFlags_RoundTripsEveryField()
    {
        UiSettingsStore sut = CreateStore();
        UiLayout expected = new(
            300,
            500,
            new WindowGeometry(10, 20, 800, 600),
            IsTagCloudVisible: false,
            LeftTabIndex: 2,
            GraphPathPrefix: @"notizen\projekt");

        sut.Save(expected);

        Assert.Equal(expected, sut.Load());
    }

    [Fact]
    public void Load_AFileFromAnEarlierVersion_KeepsTheKnownFields()
    {
        // Frühere Fassungen schrieben noch eine Breite für eine eigene Trefferspalte. Das
        // unbekannte Feld darf das Laden nicht kippen, sonst verlöre jedes Update die Breiten.
        _fileSystem.Files[FilePath] = Encoding.UTF8.GetBytes(
            """{ "FolderColumnWidth": 300, "ResultColumnWidth": 448, "PreviewColumnWidth": 500, "LeftTabIndex": 1 }""");

        Assert.Equal(new UiLayout(300, 500, LeftTabIndex: 1), CreateStore().Load());
    }

    [Fact]
    public void Save_WithoutLayout_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => CreateStore().Save(null!));
    }

    [Fact]
    public void Constructor_WithBlankPath_Throws()
    {
        _ = Assert.Throws<ArgumentException>(() => new UiSettingsStore(_fileSystem, "   ", NullLogger<UiSettingsStore>.Instance));
    }

    [Fact]
    public void Constructor_WithoutLoggerOrFileSystem_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new UiSettingsStore(_fileSystem, FilePath, null!));
        _ = Assert.Throws<ArgumentNullException>(() => new UiSettingsStore((IFileSystem)null!, FilePath, NullLogger<UiSettingsStore>.Instance));
    }

    [Fact]
    public void StorageLocation_WithAnExplicitPath_ReportsThatPath()
    {
        Assert.Equal(FilePath, CreateStore().StorageLocation, StringComparer.Ordinal);
    }

    [Fact]
    public void Constructor_WithoutAnExplicitPath_UsesTheApplicationDataDirectory()
    {
        // Der Weg ohne Pfad ist der, den die Anwendung tatsächlich nimmt.
        UiSettingsStore sut = new(_fileSystem, NullLogger<UiSettingsStore>.Instance);

        Assert.EndsWith(UiSettingsStore.LayoutFileName, sut.StorageLocation, StringComparison.Ordinal);
        Assert.True(Path.IsPathFullyQualified(sut.StorageLocation));
    }

    private UiSettingsStore CreateStore() => new(_fileSystem, FilePath, NullLogger<UiSettingsStore>.Instance);
}
