using System.IO;
using System.Text;
using MdExplorer.App.Logging;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Das Protokollfenster filtert nach Stufe und Text, wächst nicht über die Kapazität des
/// Speichers hinaus, nimmt nach dem Schließen nichts mehr an, hält den schreibenden Thread nicht
/// an und exportiert die sichtbaren Einträge samt Ausnahmetext als UTF-8 ohne Byte-Order-Mark.
/// </summary>
public sealed class LogViewerViewModelTests
{
    private const string ExportPath = @"C:\export\protokoll.log";
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 11, 30, 15, TimeSpan.Zero);

    [Fact]
    public void Ctor_LoadsExistingSnapshot()
    {
        FakeLogStore store = new(capacity: 100);
        store.Add(LogLevel.Information, "Init", "App");
        store.Add(LogLevel.Warning, "Etwas", "Mod");

        using LogViewerViewModel sut = Create(store);

        Assert.Equal(2, sut.Entries.Count);
        Assert.Equal(2, sut.VisibleCount);
    }

    [Fact]
    public void AnEntryWrittenRightAfterTheSnapshot_IsNotLost()
    {
        // Erst Momentaufnahme, dann Abonnement ließ eine Lücke: Was dazwischen geschrieben
        // wurde, stand weder in der einen noch kam es über das andere.
        FakeLogStore store = new(capacity: 100);
        store.Add(LogLevel.Information, "vorher", "App");
        store.WriteAfterSnapshot = (LogLevel.Information, "danach", "App");

        using LogViewerViewModel sut = Create(store);

        Assert.Equal(["vorher", "danach"], sut.Entries.Select(entry => entry.Message));
    }

    [Fact]
    public void AnEntryInTheSnapshotAndInTheStream_IsShownOnce()
    {
        FakeLogStore store = new(capacity: 100);
        store.WriteBeforeSnapshot = (LogLevel.Information, "beides", "App");

        using LogViewerViewModel sut = Create(store);

        Assert.Equal(["beides"], sut.Entries.Select(entry => entry.Message));
    }

    [Fact]
    public void Filter_OnHigherLevel_HidesLowerEntries()
    {
        FakeLogStore store = new(capacity: 100);
        store.Add(LogLevel.Information, "info", "src");
        store.Add(LogLevel.Warning, "warn", "src");
        store.Add(LogLevel.Error, "err", "src");

        using LogViewerViewModel sut = Create(store);
        sut.SelectedLevelFilter = sut.LevelFilters.Single(f => f.MinimumLevel == LogLevel.Warning);

        Assert.Equal(2, sut.VisibleCount);
    }

    [Fact]
    public void SearchQuery_FiltersMessageAndSourceContext()
    {
        FakeLogStore store = new(capacity: 100);
        store.Add(LogLevel.Information, "Alpha gestartet", "Indexer");
        store.Add(LogLevel.Information, "Beta beendet", "Parser");
        store.Add(LogLevel.Information, "Gamma", "Indexer");

        using LogViewerViewModel sut = Create(store);
        sut.SearchQuery = "indexer";

        Assert.Equal(2, sut.VisibleCount);
    }

    [Fact]
    public void VisibleCount_FollowsNewAndDroppedEntriesUnderAFilter()
    {
        FakeLogStore store = new(capacity: 3);
        using LogViewerViewModel sut = Create(store);
        sut.SelectedLevelFilter = sut.LevelFilters.Single(f => f.MinimumLevel == LogLevel.Warning);

        store.Add(LogLevel.Warning, "w1", "X");
        store.Add(LogLevel.Information, "i1", "X");
        store.Add(LogLevel.Warning, "w2", "X");
        store.Add(LogLevel.Information, "i2", "X");

        // Der Puffer hält drei: w1 ist herausgefallen, sichtbar bleibt nur w2.
        Assert.Equal(3, sut.Entries.Count);
        Assert.Equal(1, sut.VisibleCount);
    }

    [Fact]
    public void EntryAdded_QueuesBehindTheUiThreadInsteadOfHoldingTheWriter()
    {
        // Ein synchroner Sprung auf den UI-Thread hielt jeden protokollierenden Thread an, bis
        // die Oberfläche frei war; wartete die Oberfläche ihrerseits auf ihn, stand beides.
        FakeLogStore store = new(capacity: 100);
        QueuedUiDispatcher dispatcher = new();
        using LogViewerViewModel sut = Create(store, dispatcher);

        store.Add(LogLevel.Information, "neu", "X");

        Assert.Equal(0, dispatcher.InvokeCount);
        Assert.Empty(sut.Entries);
        dispatcher.RunPending();
        _ = Assert.Single(sut.Entries);
    }

    [Fact]
    public void ManyEntriesInARow_AreAppliedInOneTurnOfTheUiThread()
    {
        FakeLogStore store = new(capacity: 100);
        QueuedUiDispatcher dispatcher = new();
        using LogViewerViewModel sut = Create(store, dispatcher);

        store.Add(LogLevel.Information, "eins", "X");
        store.Add(LogLevel.Information, "zwei", "X");
        store.Add(LogLevel.Information, "drei", "X");

        Assert.Equal(1, dispatcher.PendingCount);
        dispatcher.RunPending();
        Assert.Equal(["eins", "zwei", "drei"], sut.Entries.Select(entry => entry.Message));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        FakeLogStore store = new(capacity: 100);
        LogViewerViewModel sut = Create(store);

        sut.Dispose();
        sut.Dispose();
    }

    [Fact]
    public void EntryAdded_AfterDispose_IsIgnored()
    {
        // Das Protokollfenster kann geschlossen werden, während im Hintergrund weiter
        // geschrieben wird. Ohne die Sperre liefe die Liste eines toten Fensters weiter voll.
        FakeLogStore store = new(capacity: 100);
        LogViewerViewModel sut = Create(store);
        sut.Dispose();

        store.Add(LogLevel.Information, "nach dem Schließen", "X");

        Assert.Empty(sut.Entries);
    }

    [Fact]
    public void AppendEntry_BeyondCapacity_DropsTheOldestEntry()
    {
        // Spiegelt die Ringpuffer-Grenze des Protokollspeichers. Ohne sie wächst die
        // Liste unbegrenzt, solange das Fenster offen bleibt.
        FakeLogStore store = new(capacity: 2);
        using LogViewerViewModel sut = Create(store);

        store.Add(LogLevel.Information, "erste", "X");
        store.Add(LogLevel.Information, "zweite", "X");
        store.Add(LogLevel.Information, "dritte", "X");

        Assert.Equal(2, sut.Entries.Count);
        Assert.Equal("zweite", sut.Entries[0].Message, StringComparer.Ordinal);
        Assert.Equal("dritte", sut.Entries[1].Message, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ExportCommand_WritesFilteredEntriesAsUtf8WithoutBom()
    {
        FakeLogStore store = new(capacity: 100);
        store.Add(LogLevel.Information, "Treffer Alpha", "Mod");
        store.Add(LogLevel.Information, "ignoriert", "Other");
        FakeFileSystem files = new();
        using LogViewerViewModel sut = Create(store, saveDialog: new ScriptedSaveDialog(ExportPath), files: files);
        sut.SearchQuery = "alpha";

        await sut.ExportCommand.ExecuteAsync(null).ConfigureAwait(true);

        byte[] bytes = files.WrittenFiles[ExportPath];
        string content = Encoding.UTF8.GetString(bytes);
        Assert.Contains("Treffer Alpha", content, StringComparison.Ordinal);
        Assert.DoesNotContain("ignoriert", content, StringComparison.Ordinal);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Contains(ExportPath, sut.ExportMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportCommand_WhenTheDialogIsCancelled_WritesNothing()
    {
        FakeLogStore store = new(capacity: 100);
        store.Add(LogLevel.Information, "Eintrag", "Mod");
        FakeFileSystem files = new();
        using LogViewerViewModel sut = Create(store, files: files);

        await sut.ExportCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Empty(files.WrittenFiles);
        Assert.Empty(sut.ExportMessage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExportCommand_WhenTheTargetCannotBeWritten_SaysSoInsteadOfCrashing(bool accessDenied)
    {
        // Ein schreibgeschütztes Ziel, eine im Editor geöffnete Datei, eine volle Platte: Die
        // Ausnahme lief früher aus dem Befehl heraus und beendete die Anwendung.
        FakeLogStore store = new(capacity: 100);
        store.Add(LogLevel.Information, "Eintrag", "Mod");
        FakeFileSystem files = new()
        {
            FailOnWrite = accessDenied ? new UnauthorizedAccessException("Zugriff verweigert") : new IOException("Datenträger voll"),
        };
        using LogViewerViewModel sut = Create(store, saveDialog: new ScriptedSaveDialog(ExportPath), files: files);

        await sut.ExportCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Contains(accessDenied ? "Zugriff verweigert" : "Datenträger voll", sut.ExportMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportCommand_ProposesAFileNameFromTheClock()
    {
        FakeLogStore store = new(capacity: 100);
        ScriptedSaveDialog dialog = new(ExportPath);
        using LogViewerViewModel sut = Create(store, saveDialog: dialog);

        await sut.ExportCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal("mdexplorer-log-20261025-113015.log", dialog.ProposedName);
    }

    [Fact]
    public async Task ExportCommand_WritesEveryLevelAbbreviationAndTheExceptionText()
    {
        FakeLogStore store = new(capacity: 100);
        store.Add(LogLevel.Trace, "spur", "Mod");
        store.Add(LogLevel.Debug, "fehlersuche", "Mod");
        store.Add(LogLevel.Error, "fehler", "Mod", "System.InvalidOperationException: kaputt");
        store.Add(LogLevel.Critical, "kritisch", "Mod");
        store.Add((LogLevel)99, "unbekannt", "Mod");
        FakeFileSystem files = new();
        using LogViewerViewModel sut = Create(store, saveDialog: new ScriptedSaveDialog(ExportPath), files: files);
        sut.SelectedLevelFilter = sut.LevelFilters.Single(f => f.MinimumLevel == LogLevel.Trace);

        await sut.ExportCommand.ExecuteAsync(null).ConfigureAwait(true);

        string content = Encoding.UTF8.GetString(files.WrittenFiles[ExportPath]);
        Assert.Contains("[TRC]", content, StringComparison.Ordinal);
        Assert.Contains("[DBG]", content, StringComparison.Ordinal);
        Assert.Contains("[ERR]", content, StringComparison.Ordinal);
        Assert.Contains("[CRT]", content, StringComparison.Ordinal);
        Assert.Contains("[???]", content, StringComparison.Ordinal);
        // Der Ausnahmetext ist der eigentliche Grund, warum jemand ein Protokoll exportiert.
        Assert.Contains("kaputt", content, StringComparison.Ordinal);
    }

    private static LogViewerViewModel Create(
        FakeLogStore store,
        IUiDispatcher? dispatcher = null,
        IFileSaveDialogService? saveDialog = null,
        FakeFileSystem? files = null) =>
        new(
            store,
            dispatcher ?? new ImmediateUiDispatcher(),
            saveDialog ?? new ScriptedSaveDialog(null),
            files ?? new FakeFileSystem(),
            new FakeTimeProvider(Now),
            NullLogger<LogViewerViewModel>.Instance);

    /// <summary>Protokollspeicher mit frei wählbarer Kapazität, die er selbst nicht durchsetzt.</summary>
    private sealed class FakeLogStore(int capacity) : IMemoryLogStore
    {
        private readonly List<LogEntry> _entries = [];

        public int Capacity { get; } = capacity;

        public event EventHandler<LogEntry>? EntryAdded;

        /// <summary>Ein Eintrag, der geschrieben wird, nachdem die Momentaufnahme kopiert ist.</summary>
        public (LogLevel Level, string Message, string Source)? WriteAfterSnapshot { get; set; }

        /// <summary>Ein Eintrag, der beim Aufruf der Momentaufnahme noch vor dem Kopieren geschrieben wird.</summary>
        public (LogLevel Level, string Message, string Source)? WriteBeforeSnapshot { get; set; }

        public void Add(LogLevel level, string message, string source) => Add(level, message, source, exception: null);

        public void Add(LogLevel level, string message, string source, string? exception)
        {
            LogEntry entry = new(Now, level, source, message, exception);
            _entries.Add(entry);
            EntryAdded?.Invoke(this, entry);
        }

        public IReadOnlyList<LogEntry> Snapshot()
        {
            if (WriteBeforeSnapshot is { } before)
            {
                WriteBeforeSnapshot = null;
                Add(before.Level, before.Message, before.Source);
            }
            LogEntry[] copy = [.. _entries];
            if (WriteAfterSnapshot is { } after)
            {
                WriteAfterSnapshot = null;
                Add(after.Level, after.Message, after.Source);
            }
            return copy;
        }
    }

    /// <summary>Speicherdialog, der den vorgegebenen Pfad wählt oder ohne Pfad abbricht.</summary>
    private sealed class ScriptedSaveDialog(string? path) : IFileSaveDialogService
    {
        public string? ProposedName { get; private set; }

        public string? PromptForSavePath(string defaultFileName, string filter)
        {
            ProposedName = defaultFileName;
            return path;
        }
    }
}
