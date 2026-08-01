using System.IO;
using MdExplorer.App.Logging;
using MdExplorer.App.Services;
using MdExplorer.App.ViewModels;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>Tests für das ViewModel des integrierten Log-Viewers.</summary>
public sealed class LogViewerViewModelTests
{
    [Fact]
    public void Ctor_LoadsExistingSnapshot()
    {
        FakeLogStore store = new(capacity: 100);
        store.Add(LogLevel.Information, "Init", "App");
        store.Add(LogLevel.Warning, "Etwas", "Mod");

        using LogViewerViewModel sut = new(store, new ImmediateDispatcher(), new NullSaveDialog());

        Assert.Equal(2, sut.Entries.Count);
    }

    [Fact]
    public void Filter_OnHigherLevel_HidesLowerEntries()
    {
        FakeLogStore store = new(capacity: 100);
        store.Add(LogLevel.Information, "info", "src");
        store.Add(LogLevel.Warning, "warn", "src");
        store.Add(LogLevel.Error, "err", "src");

        using LogViewerViewModel sut = new(store, new ImmediateDispatcher(), new NullSaveDialog());
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

        using LogViewerViewModel sut = new(store, new ImmediateDispatcher(), new NullSaveDialog());
        sut.SearchQuery = "indexer";

        Assert.Equal(2, sut.VisibleCount);
    }

    [Fact]
    public void EntryAdded_AppendsThroughDispatcher()
    {
        FakeLogStore store = new(capacity: 100);
        ImmediateDispatcher dispatcher = new();
        using LogViewerViewModel sut = new(store, dispatcher, new NullSaveDialog());

        store.Add(LogLevel.Information, "neu", "X");

        Assert.Equal(1, dispatcher.InvokeCount);
        _ = Assert.Single(sut.Entries);
    }

    [Fact]
    public async Task ExportCommand_WritesFilteredEntriesAsUtf8WithoutBom()
    {
        FakeLogStore store = new(capacity: 100);
        store.Add(LogLevel.Information, "Treffer Alpha", "Mod");
        store.Add(LogLevel.Information, "ignoriert", "Other");

        string tempFile = Path.Combine(Path.GetTempPath(), "mdexp-log-export-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            using LogViewerViewModel sut = new(
                store,
                new ImmediateDispatcher(),
                new ScriptedSaveDialog(tempFile));
            sut.SearchQuery = "alpha";

            await sut.ExportCommand.ExecuteAsync(null).ConfigureAwait(true);

            Assert.True(File.Exists(tempFile));
            string content = await File.ReadAllTextAsync(tempFile).ConfigureAwait(true);
            Assert.Contains("Treffer Alpha", content, StringComparison.Ordinal);
            Assert.DoesNotContain("ignoriert", content, StringComparison.Ordinal);
            byte[] bytes = await File.ReadAllBytesAsync(tempFile).ConfigureAwait(true);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        FakeLogStore store = new(capacity: 100);
        LogViewerViewModel sut = new(store, new ImmediateDispatcher(), new NullSaveDialog());

        sut.Dispose();
        sut.Dispose();
    }

    [Fact]
    public void EntryAdded_AfterDispose_IsIgnored()
    {
        // Das Protokollfenster kann geschlossen werden, während im Hintergrund weiter
        // geschrieben wird. Ohne die Sperre liefe die Liste eines toten Fensters weiter voll.
        FakeLogStore store = new(capacity: 100);
        LogViewerViewModel sut = new(store, new ImmediateDispatcher(), new NullSaveDialog());
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
        using LogViewerViewModel sut = new(store, new ImmediateDispatcher(), new NullSaveDialog());

        store.Add(LogLevel.Information, "erste", "X");
        store.Add(LogLevel.Information, "zweite", "X");
        store.Add(LogLevel.Information, "dritte", "X");

        Assert.Equal(2, sut.Entries.Count);
        Assert.Equal("zweite", sut.Entries[0].Message, StringComparer.Ordinal);
        Assert.Equal("dritte", sut.Entries[1].Message, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ExportCommand_WhenTheDialogIsCancelled_WritesNothing()
    {
        FakeLogStore store = new(capacity: 100);
        store.Add(LogLevel.Information, "Eintrag", "Mod");
        using LogViewerViewModel sut = new(store, new ImmediateDispatcher(), new NullSaveDialog());

        await sut.ExportCommand.ExecuteAsync(null).ConfigureAwait(true);

        // Kein Wurf, keine Datei — der Abbruch ist der Normalfall.
        _ = Assert.Single(sut.Entries);
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

        string zielDatei = Path.Combine(Path.GetTempPath(), "mdexp-log-export-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            using LogViewerViewModel sut = new(store, new ImmediateDispatcher(), new ScriptedSaveDialog(zielDatei));
            sut.SelectedLevelFilter = sut.LevelFilters.Single(f => f.MinimumLevel == LogLevel.Trace);

            await sut.ExportCommand.ExecuteAsync(null).ConfigureAwait(true);

            string inhalt = await File.ReadAllTextAsync(zielDatei).ConfigureAwait(true);
            Assert.Contains("[TRC]", inhalt, StringComparison.Ordinal);
            Assert.Contains("[DBG]", inhalt, StringComparison.Ordinal);
            Assert.Contains("[ERR]", inhalt, StringComparison.Ordinal);
            Assert.Contains("[CRT]", inhalt, StringComparison.Ordinal);
            Assert.Contains("[???]", inhalt, StringComparison.Ordinal);
            // Der Ausnahmetext ist der eigentliche Grund, warum jemand ein Protokoll exportiert.
            Assert.Contains("kaputt", inhalt, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(zielDatei))
            {
                File.Delete(zielDatei);
            }
        }
    }

    private sealed class FakeLogStore : IMemoryLogStore
    {
        private readonly List<LogEntry> _entries = [];

        public FakeLogStore(int capacity)
        {
            Capacity = capacity;
        }

        public int Capacity { get; }

        public event EventHandler<LogEntry>? EntryAdded;

        public void Add(LogLevel level, string message, string source) => Add(level, message, source, exception: null);

        public void Add(LogLevel level, string message, string source, string? exception)
        {
            LogEntry entry = new(DateTimeOffset.UtcNow, level, source, message, exception);
            _entries.Add(entry);
            EntryAdded?.Invoke(this, entry);
        }

        public IReadOnlyList<LogEntry> Snapshot() => [.. _entries];
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public int InvokeCount { get; private set; }

        public void Invoke(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            InvokeCount++;
            action();
        }
    }

    private sealed class NullSaveDialog : IFileSaveDialogService
    {
        public string? PromptForSavePath(string defaultFileName, string filter) => null;
    }

    private sealed class ScriptedSaveDialog : IFileSaveDialogService
    {
        private readonly string _path;

        public ScriptedSaveDialog(string path) => _path = path;

        public string? PromptForSavePath(string defaultFileName, string filter) => _path;
    }
}
