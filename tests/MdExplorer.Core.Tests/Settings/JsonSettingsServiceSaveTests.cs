using MdExplorer.Core.Models;
using MdExplorer.Core.Settings;
using MdExplorer.Core.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;
using static MdExplorer.Core.Tests.Settings.SettingsServiceFactory;

namespace MdExplorer.Core.Tests.Settings;

/// <summary>
/// Gespeichert wird verlustfrei und atomar; nur eine echte Änderung hinterlässt einen Eintrag im
/// Verlauf, und ein Fehler im Verlauf macht ein gelungenes Speichern nicht nachträglich zum
/// Fehlschlag.
/// </summary>
public sealed class JsonSettingsServiceSaveTests
{
    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsValues()
    {
        InMemoryFileSystem fileSystem = new();
        AppSettings input = new(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings([@"C:\Notes"], ["**/Drafts/**", "!Drafts/keep.md"], [@"C:\Notes\Drafts\Archive"], false),
            new AppearanceSettings(AppTheme.Dark, 18, 75),
            new BehaviorSettings(450, 900));

        using JsonSettingsService writer = Create(fileSystem);
        await writer.SaveAsync(input, TestContext.Current.CancellationToken).ConfigureAwait(true);
        using JsonSettingsService reader = Create(fileSystem);
        AppSettings result = await reader.LoadAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(input.Indexing.Roots, result.Indexing.Roots);
        Assert.Equal(input.Indexing.ExclusionPatterns, result.Indexing.ExclusionPatterns);
        Assert.Equal(input.Indexing.UiExcludedFolders, result.Indexing.UiExcludedFolders);
        Assert.False(result.Indexing.AutoExtractHashtags);
        Assert.Equal(AppTheme.Dark, result.Appearance.Theme);
        Assert.Equal(18, result.Appearance.PreviewFontSize);
        Assert.Equal(75, result.Appearance.ResultsPerPage);
        Assert.Equal(450, result.Behavior.SearchDebounceMs);
        Assert.Equal(900, result.Behavior.IndexerResyncIntervalSeconds);
    }

    [Fact]
    public async Task SaveAsync_WritesThroughTheAtomicPath()
    {
        // Ein halb geschriebenes settings.json nach einem Absturz kostet alle Einstellungen. Der
        // atomare Weg schreibt erst vollständig, dann wird ersetzt.
        InMemoryFileSystem fileSystem = new();
        using JsonSettingsService sut = Create(fileSystem);

        await sut.SaveAsync(AppSettings.Default, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, fileSystem.AtomicWrites);
        Assert.True(fileSystem.FileExists(SettingsPath));
    }

    [Fact]
    public async Task SaveAsync_WithAValueOutsideItsRange_RefusesAndLeavesTheFile()
    {
        // Ein Aufrufer ohne Dialog (Kontextmenü des Ordnerbaums) geht an dessen Prüfung vorbei.
        // Beim Laden würde der Wert still ersetzt; der Fehler des Aufrufers bliebe unsichtbar.
        InMemoryFileSystem fileSystem = new();
        using JsonSettingsService sut = Create(fileSystem);
        AppSettings invalid = AppSettings.Default with { Behavior = AppSettings.Default.Behavior with { SearchDebounceMs = 0 } };

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => sut.SaveAsync(invalid, TestContext.Current.CancellationToken)).ConfigureAwait(true);

        Assert.False(fileSystem.FileExists(SettingsPath));
        Assert.Equal(BehaviorSettings.Default.SearchDebounceMs, sut.Current.Behavior.SearchDebounceMs);
    }

    [Fact]
    public async Task SaveAsync_WhenTheHistoryFails_StillSavesAndAnnouncesTheChange()
    {
        // Die Datei ist schon ersetzt, wenn der Verlauf schreibt. Scheitert er, darf der Aufrufer
        // keine Ausnahme sehen und das Ereignis nicht ausbleiben; sonst liefen Indexer und
        // Oberfläche bis zum Neustart mit dem alten Stand.
        InMemoryFileSystem fileSystem = new();
        fileSystem.FailWritesOf(AuditPath, new IOException("Platte voll"));
        using FileSystemSettingsHistoryStore history = CreateHistory(fileSystem);
        using JsonSettingsService sut = Create(fileSystem, history);
        QueueingSynchronizationContext context = new();
        int announced = 0;
        sut.SettingsChanged += (_, _) => announced++;
        AppSettings dark = AppSettings.Default with { Appearance = AppearanceSettings.Default with { Theme = AppTheme.Dark } };

        await context.RunWithinAsync(() => sut.SaveAsync(dark, TestContext.Current.CancellationToken)).ConfigureAwait(true);
        context.DrainPending();

        Assert.Equal(AppTheme.Dark, sut.Current.Appearance.Theme);
        Assert.Equal(1, announced);
    }

    [Fact]
    public async Task SaveAsync_OnThreeDistinctSaves_WritesThreeSnapshotsAndThreeAuditLines()
    {
        InMemoryFileSystem fileSystem = new();
        FakeTimeProvider clock = new(Start);
        using FileSystemSettingsHistoryStore history = CreateHistory(fileSystem);
        using JsonSettingsService sut = Create(fileSystem, history, clock);

        AppSettings state1 = AppSettings.Default with { Behavior = AppSettings.Default.Behavior with { SearchDebounceMs = 400 } };
        await sut.SaveAsync(state1, TestContext.Current.CancellationToken).ConfigureAwait(true);
        clock.Advance(TimeSpan.FromSeconds(2));
        AppSettings state2 = state1 with { Appearance = state1.Appearance with { Theme = AppTheme.Dark } };
        await sut.SaveAsync(state2, TestContext.Current.CancellationToken).ConfigureAwait(true);
        clock.Advance(TimeSpan.FromSeconds(3));
        AppSettings state3 = state2 with { Indexing = state2.Indexing with { Roots = [@"C:\Notes"] } };
        await sut.SaveAsync(state3, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(3, fileSystem.EnumerateFiles(HistoryDirectory, "settings.*.json", recursive: false).Count());
        string[] auditLines = fileSystem.Text(AuditPath).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, auditLines.Length);
        Assert.Contains("\"behavior.searchDebounceMs\"", auditLines[0], StringComparison.Ordinal);
        Assert.Contains("\"appearance.theme\"", auditLines[1], StringComparison.Ordinal);
        Assert.Contains("\"indexing.roots[0]\"", auditLines[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAsync_OnContentEquivalentButFreshListInstance_WritesNoSecondHistoryEntry()
    {
        // IReadOnlyList<string>-Felder vergleicht ein Record über die Referenz. Der Dialog erzeugt
        // bei jedem OK neue Listen; ohne strukturellen Vergleich gälte jedes Speichern als Änderung.
        InMemoryFileSystem fileSystem = new();
        using FileSystemSettingsHistoryStore history = CreateHistory(fileSystem);
        using JsonSettingsService sut = Create(fileSystem, history);

        await sut.SaveAsync(AppSettings.Default with { Indexing = AppSettings.Default.Indexing with { Roots = [@"F:\Notes"] } }, TestContext.Current.CancellationToken).ConfigureAwait(true);
        await sut.SaveAsync(AppSettings.Default with { Indexing = AppSettings.Default.Indexing with { Roots = [@"F:\Notes"] } }, TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = Assert.Single(fileSystem.EnumerateFiles(HistoryDirectory, "settings.*.json", recursive: false));
        _ = Assert.Single(fileSystem.Text(AuditPath).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task SaveAsync_OnIdenticalPayload_WritesNoHistory()
    {
        InMemoryFileSystem fileSystem = new();
        using FileSystemSettingsHistoryStore history = CreateHistory(fileSystem);
        using JsonSettingsService sut = Create(fileSystem, history);

        await sut.SaveAsync(AppSettings.Default, TestContext.Current.CancellationToken).ConfigureAwait(true);
        await sut.SaveAsync(AppSettings.Default, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(fileSystem.DirectoryExists(HistoryDirectory));
        Assert.False(fileSystem.FileExists(AuditPath));
    }

    /// <remarks>
    /// Ein zweites Freigeben darf nicht zum Fehler werden. Der Dienst hängt am Anwendungs-
    /// Container; dass der ihn genau einmal freigibt, ist eine Annahme, keine Zusicherung.
    /// </remarks>
    [Fact]
    public void Dispose_CalledTwice_IsHarmless()
    {
        JsonSettingsService sut = Create(new InMemoryFileSystem());

        sut.Dispose();
        sut.Dispose();

        Assert.Equal(AppSettings.CurrentSchemaVersion, sut.Current.SchemaVersion);
    }

    [Fact]
    public async Task SaveAsync_WhenCancelled_ThrowsAndKeepsFileAndState()
    {
        InMemoryFileSystem fileSystem = new();
        using JsonSettingsService sut = Create(fileSystem);
        await sut.SaveAsync(AppSettings.Default, TestContext.Current.CancellationToken).ConfigureAwait(true);
        AppSettings changed = AppSettings.Default with { Appearance = AppSettings.Default.Appearance with { PreviewFontSize = 20 } };
        using CancellationTokenSource cts = new();
        await cts.CancelAsync().ConfigureAwait(true);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.SaveAsync(changed, cts.Token)).ConfigureAwait(true);

        Assert.Equal(1, fileSystem.AtomicWrites);
        Assert.Equal(AppSettings.Default.Appearance.PreviewFontSize, sut.Current.Appearance.PreviewFontSize);
    }

    /// <remarks>
    /// Die Gegenprobe zum Test darüber: Nach dem Freigeben darf ein Speichern nicht
    /// stillschweigend ins Leere laufen.
    /// </remarks>
    [Fact]
    public async Task SaveAsync_AfterDispose_Fails()
    {
        JsonSettingsService sut = Create(new InMemoryFileSystem());
        sut.Dispose();

        _ = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => sut.SaveAsync(AppSettings.Default, TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }
}
