using MdExplorer.Core.Models;
using MdExplorer.Core.Settings;
using MdExplorer.Core.Tests.Fakes;
using static MdExplorer.Core.Tests.Settings.SettingsServiceFactory;

namespace MdExplorer.Core.Tests.Settings;

/// <summary>
/// Eine fehlende, kaputte oder gesperrte Einstellungsdatei kostet nie den Start; es gelten dann
/// die Voreinstellungen. Was der Nutzer von Hand eingetragen hat, geht dabei nicht verloren.
/// </summary>
public sealed class JsonSettingsServiceLoadTests
{
    [Fact]
    public async Task LoadAsync_OnMissingFile_ReturnsDefaults()
    {
        using JsonSettingsService sut = Create(new InMemoryFileSystem());

        AppSettings result = await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AppSettings.CurrentSchemaVersion, result.SchemaVersion);
        Assert.Empty(result.Indexing.Roots);
        Assert.Equal(IndexingSettings.DefaultExclusionPatterns, result.Indexing.ExclusionPatterns);
        Assert.Empty(result.Indexing.UiExcludedFolders);
        Assert.Equal(AppTheme.System, result.Appearance.Theme);
    }

    [Fact]
    public async Task LoadAsync_OnJsonNull_ReturnsDefaults()
    {
        // "null" ist gültiges JSON, ergibt aber keine Einstellungen. Ohne die Absicherung
        // liefe die Anwendung mit einem Nullwert weiter und fällt beim ersten Zugriff um.
        InMemoryFileSystem fileSystem = new();
        fileSystem.AddFile(SettingsPath, "null");
        using JsonSettingsService sut = Create(fileSystem);

        AppSettings result = await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result.Indexing.Roots);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task LoadAsync_WhenTheFileCannotBeRead_ReturnsDefaults(Type failure)
    {
        // Gesperrt, ein Verzeichnis an Stelle der Datei, fehlende Rechte: Der Start darf daran
        // nicht scheitern.
        InMemoryFileSystem fileSystem = new();
        fileSystem.AddFile(SettingsPath, "{}");
        fileSystem.FailReadsOf(SettingsPath, (Exception)Activator.CreateInstance(failure, "gesperrt")!);
        using JsonSettingsService sut = Create(fileSystem);

        AppSettings result = await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result.Indexing.Roots);
    }

    [Fact]
    public async Task LoadAsync_OnInvalidJson_ReturnsDefaultsAndKeepsACopyOfTheFile()
    {
        // Ohne Sicherung überschriebe das nächste Speichern die kaputte Datei, und mit ihr alles,
        // was der Nutzer von Hand eingetragen hatte und nur um ein Komma verfehlt hat.
        InMemoryFileSystem fileSystem = new();
        fileSystem.AddFile(SettingsPath, "{ \"indexing\": { \"roots\": [\"C:\\\\Notizen\" ] ");
        using JsonSettingsService sut = Create(fileSystem);

        AppSettings result = await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result.Indexing.Roots);
        string backup = Assert.Single(fileSystem.FilePaths, path => path.Contains(".corrupt-", StringComparison.Ordinal));
        Assert.Equal(fileSystem.Text(SettingsPath), fileSystem.Text(backup));
    }

    [Fact]
    public async Task LoadAsync_WithValuesOutsideTheirRange_FallsBackToTheDefaults()
    {
        // Eine von Hand bearbeitete Datei geht am Einstellungsdialog vorbei. Ein Such-Debounce
        // von 0 ließe nach jedem Tastendruck eine Suche laufen, eine Schriftgröße von 2 Pixel
        // machte die Vorschau unlesbar.
        InMemoryFileSystem fileSystem = new();
        fileSystem.AddFile(SettingsPath, """
            {
              "schemaVersion": 3,
              "indexing": { "roots": [], "exclusionPatterns": [], "uiExcludedFolders": [], "autoExtractHashtags": true },
              "appearance": { "theme": "Dark", "previewFontSize": 2, "resultsPerPage": 100000 },
              "behavior": { "searchDebounceMs": 0, "indexerResyncIntervalSeconds": -5, "checkForUpdatesAtStartup": false }
            }
            """);
        using JsonSettingsService sut = Create(fileSystem);

        AppSettings result = await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AppearanceSettings.Default.PreviewFontSize, result.Appearance.PreviewFontSize);
        Assert.Equal(AppearanceSettings.Default.ResultsPerPage, result.Appearance.ResultsPerPage);
        Assert.Equal(BehaviorSettings.Default.SearchDebounceMs, result.Behavior.SearchDebounceMs);
        Assert.Equal(BehaviorSettings.Default.IndexerResyncIntervalSeconds, result.Behavior.IndexerResyncIntervalSeconds);
        // Was in seinen Grenzen liegt, bleibt, wie es in der Datei steht.
        Assert.Equal(AppTheme.Dark, result.Appearance.Theme);
        Assert.False(result.Behavior.CheckForUpdatesAtStartup);
    }

    [Fact]
    public async Task LoadAsync_WithValuesAtTheirLimits_KeepsThem()
    {
        InMemoryFileSystem fileSystem = new();
        fileSystem.AddFile(SettingsPath, """
            {
              "schemaVersion": 3,
              "appearance": { "theme": "Light", "previewFontSize": 64, "resultsPerPage": 10 },
              "behavior": { "searchDebounceMs": 5000, "indexerResyncIntervalSeconds": 0 }
            }
            """);
        using JsonSettingsService sut = Create(fileSystem);

        AppSettings result = await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(64, result.Appearance.PreviewFontSize);
        Assert.Equal(10, result.Appearance.ResultsPerPage);
        Assert.Equal(5_000, result.Behavior.SearchDebounceMs);
        Assert.Equal(0, result.Behavior.IndexerResyncIntervalSeconds);
    }

    [Fact]
    public async Task LoadAsync_AFileFromBeforeTheUpdateCheck_HasTheCheckSwitchedOn()
    {
        // Schema 2 kannte die Update-Prüfung nicht; fehlt das Feld, gilt die Vorgabe.
        InMemoryFileSystem fileSystem = new();
        fileSystem.AddFile(SettingsPath, """{ "schemaVersion": 2, "behavior": { "searchDebounceMs": 300, "indexerResyncIntervalSeconds": 300 } }""");
        using JsonSettingsService sut = Create(fileSystem);

        AppSettings result = await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Behavior.CheckForUpdatesAtStartup);
    }

    [Fact]
    public async Task SaveAsync_AfterLoadingANewerSchema_KeepsACopyOfTheNewerFileFirst()
    {
        // Nach einem Wechsel auf eine ältere Fassung kennt diese nicht alle Felder. Speichert sie
        // ohne Sicherung, sind die unbekannten Felder still weg, auch nach der Rückkehr.
        InMemoryFileSystem fileSystem = new();
        const string newer = """{ "schemaVersion": 9, "futureField": "wichtig", "behavior": { "searchDebounceMs": 400, "indexerResyncIntervalSeconds": 300 } }""";
        fileSystem.AddFile(SettingsPath, newer);
        using JsonSettingsService sut = Create(fileSystem);
        AppSettings loaded = await sut.LoadAsync(TestContext.Current.CancellationToken);

        await sut.SaveAsync(loaded with { Appearance = loaded.Appearance with { Theme = AppTheme.Dark } }, TestContext.Current.CancellationToken);

        string backup = Assert.Single(fileSystem.FilePaths, path => path.Contains(".schema-", StringComparison.Ordinal));
        Assert.Equal(newer, fileSystem.Text(backup));
        Assert.Equal(400, loaded.Behavior.SearchDebounceMs);
    }

    /// <remarks>
    /// Wer die Einstellungen liest, bevor die Datei gelesen wurde (beim Hochfahren nicht
    /// ungewöhnlich), darf keinen Nullwert bekommen.
    /// </remarks>
    [Fact]
    public void Current_BeforeTheFirstLoad_IsTheDefaultSet()
    {
        using JsonSettingsService sut = Create(new InMemoryFileSystem());

        Assert.Equal(AppSettings.CurrentSchemaVersion, sut.Current.SchemaVersion);
        Assert.Empty(sut.Current.Indexing.Roots);
    }
}
