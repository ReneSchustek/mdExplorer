using System.IO;
using MdExplorer.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Prüft, dass eine beschädigte oder unzugängliche Layout-Datei den Programmstart nicht
/// verhindert. Der Speicher liegt in <c>%LOCALAPPDATA%</c> und kann durch einen abgebrochenen
/// Schreibvorgang halb geschrieben zurückbleiben — würde das Laden dann werfen, käme die
/// Anwendung nie bis zum Hauptfenster.
/// </summary>
public sealed class UiSettingsStoreRobustnessTests : IDisposable
{
    private readonly string _ordner = Path.Combine(Path.GetTempPath(), "mdexp-ui-" + Guid.NewGuid().ToString("N"));

    private string DateiPfad => Path.Combine(_ordner, UiSettingsStore.LayoutFileName);

    [Fact]
    public void Load_OnCorruptJson_FallsBackToTheDefaultLayout()
    {
        _ = Directory.CreateDirectory(_ordner);
        File.WriteAllText(DateiPfad, "{ das ist kein json");
        UiSettingsStore sut = new(DateiPfad, NullLogger<UiSettingsStore>.Instance);

        UiLayout layout = sut.Load();

        Assert.Equal(UiLayout.Default, layout);
    }

    [Fact]
    public void Load_OnJsonNull_FallsBackToTheDefaultLayout()
    {
        // "null" ist gültiges JSON, ergibt aber kein Layout — ohne die Absicherung
        // liefe die Oberfläche mit Breite 0 an.
        _ = Directory.CreateDirectory(_ordner);
        File.WriteAllText(DateiPfad, "null");
        UiSettingsStore sut = new(DateiPfad, NullLogger<UiSettingsStore>.Instance);

        UiLayout layout = sut.Load();

        Assert.Equal(UiLayout.Default, layout);
    }

    [Fact]
    public void Load_WhenThePathIsADirectory_FallsBackToTheDefaultLayout()
    {
        // Ein Verzeichnis an Stelle der Datei löst beim Öffnen einen E/A-Fehler aus.
        _ = Directory.CreateDirectory(DateiPfad);
        UiSettingsStore sut = new(DateiPfad, NullLogger<UiSettingsStore>.Instance);

        UiLayout layout = sut.Load();

        Assert.Equal(UiLayout.Default, layout);
    }

    [Fact]
    public void Save_WhenThePathIsADirectory_DoesNotThrow()
    {
        // Speichern läuft beim Schließen des Fensters. Ein Fehler darf das Beenden nicht stören.
        _ = Directory.CreateDirectory(DateiPfad);
        UiSettingsStore sut = new(DateiPfad, NullLogger<UiSettingsStore>.Instance);

        sut.Save(UiLayout.Default);
    }

    [Fact]
    public void Load_WhenTheFileIsLockedByAnotherProcess_FallsBackToTheDefaultLayout()
    {
        // Kommt beim Start vor, wenn eine zweite Programm-Instanz oder ein Virenscanner die
        // Datei gerade hält. Der Start darf daran nicht scheitern.
        _ = Directory.CreateDirectory(_ordner);
        File.WriteAllText(DateiPfad, "{}");
        UiSettingsStore sut = new(DateiPfad, NullLogger<UiSettingsStore>.Instance);

        using (new FileStream(DateiPfad, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            UiLayout layout = sut.Load();

            Assert.Equal(UiLayout.Default, layout);
        }
    }

    [Fact]
    public void Save_WhenTheFileIsLockedByAnotherProcess_DoesNotThrow()
    {
        // Gespeichert wird beim Schließen des Fensters. Ein Schreibfehler darf das
        // Beenden nicht aufhalten — der Verlust der Spaltenbreiten ist verschmerzbar.
        _ = Directory.CreateDirectory(_ordner);
        File.WriteAllText(DateiPfad, "{}");
        UiSettingsStore sut = new(DateiPfad, NullLogger<UiSettingsStore>.Instance);

        using (new FileStream(DateiPfad, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            sut.Save(new UiLayout(300, 400, 500));
        }
    }

    [Fact]
    public void Save_OnAMissingDirectory_CreatesItFirst()
    {
        UiSettingsStore sut = new(DateiPfad, NullLogger<UiSettingsStore>.Instance);

        sut.Save(new UiLayout(300, 400, 500));

        Assert.True(File.Exists(DateiPfad));
    }

    [Fact]
    public void SaveAndLoad_WithWindowGeometryAndFlags_RoundTripsEveryField()
    {
        UiSettingsStore sut = new(DateiPfad, NullLogger<UiSettingsStore>.Instance);
        UiLayout erwartet = new(
            300,
            400,
            500,
            new WindowGeometry(10, 20, 800, 600),
            IsTagCloudVisible: false,
            LeftTabIndex: 2,
            GraphPathPrefix: @"notizen\projekt");

        sut.Save(erwartet);

        Assert.Equal(erwartet, sut.Load());
    }

    [Fact]
    public void Save_WithoutLayout_Throws()
    {
        UiSettingsStore sut = new(DateiPfad, NullLogger<UiSettingsStore>.Instance);

        _ = Assert.Throws<ArgumentNullException>(() => sut.Save(null!));
    }

    [Fact]
    public void Constructor_WithBlankPath_Throws()
    {
        _ = Assert.Throws<ArgumentException>(() => new UiSettingsStore("   ", NullLogger<UiSettingsStore>.Instance));
    }

    [Fact]
    public void Constructor_WithoutLogger_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new UiSettingsStore(DateiPfad, null!));
    }

    [Fact]
    public void StorageLocation_ReportsTheConfiguredPath()
    {
        UiSettingsStore sut = new(DateiPfad, NullLogger<UiSettingsStore>.Instance);

        Assert.Equal(DateiPfad, sut.StorageLocation, StringComparer.Ordinal);
    }

    [Fact]
    public void Constructor_WithoutAnExplicitPath_UsesTheApplicationDataDirectory()
    {
        // Der parameterlose Weg ist der, den die Anwendung tatsächlich nimmt.
        UiSettingsStore sut = new(NullLogger<UiSettingsStore>.Instance);

        Assert.EndsWith(UiSettingsStore.LayoutFileName, sut.StorageLocation, StringComparison.Ordinal);
        Assert.True(Path.IsPathFullyQualified(sut.StorageLocation));
    }

    public void Dispose()
    {
        if (Directory.Exists(_ordner))
        {
            Directory.Delete(_ordner, recursive: true);
        }
    }
}
