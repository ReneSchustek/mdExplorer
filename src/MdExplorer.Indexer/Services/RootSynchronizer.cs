using System.Data.Common;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.Models;
using MdExplorer.Indexer.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Indexer.Services;

/// <summary>Wofür eine Wurzel abgeglichen wird.</summary>
internal enum SyncMode
{
    /// <summary>Erst-Scan: Fortschritt wird immer gemeldet, die Oberfläche füllt sich daran.</summary>
    Initial,

    /// <summary>Abgleich im Hintergrund: Fortschritt nur, wenn sich etwas geändert hat.</summary>
    Background,
}

/// <summary>
/// Gleicht eine Wurzel mit dem Index ab: neue und geänderte Dateien schreiben, Einträge
/// verschwundener Dateien entfernen.
/// </summary>
/// <remarks>
/// <para>
/// Der Bestand der Wurzel wird einmal als Ganzes gelesen und im Speicher verglichen. Eine Abfrage
/// je Datei kostete bei 7.700 Notizen 7.700 Abfragen je Abgleich, auch wenn sich nichts geändert hat.
/// </para>
/// <para>
/// Ein halb gelesener Baum entfernt nichts: Bricht die Aufzählung ab, kennt die Liste nur einen
/// Teil des Bestands, und alles Ungelesene sähe wie gelöscht aus.
/// </para>
/// </remarks>
internal sealed partial class RootSynchronizer
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileScanner _scanner;
    private readonly IndexEntryWriter _writer;
    private readonly IndexerOptions _options;
    private readonly Action<IndexerScanProgressEventArgs> _reportProgress;
    private readonly ILogger _logger;

    public RootSynchronizer(
        IServiceScopeFactory scopeFactory,
        IFileScanner scanner,
        IndexEntryWriter writer,
        IndexerOptions options,
        Action<IndexerScanProgressEventArgs> reportProgress,
        ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _scanner = scanner;
        _writer = writer;
        _options = options;
        _reportProgress = reportProgress;
        _logger = logger;
    }

    /// <summary>Gleicht <paramref name="root"/> ab und liefert die Zahl der gefundenen Dateien.</summary>
    public async Task<int> SyncAsync(string root, SyncMode mode, CancellationToken cancellationToken)
    {
        Dictionary<string, MarkdownFile> stored = await LoadStoredAsync(root, cancellationToken).ConfigureAwait(false);
        ScanState state = new(mode);
        await ScanAsync(root, stored, state, cancellationToken).ConfigureAwait(false);

        if (state.EnumerationCompleted)
        {
            await RemoveVanishedAsync(root, stored, state, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            LogCleanupSkipped(_logger, root, state.FilesOnDisk.Count);
        }

        if (mode == SyncMode.Initial || state.Changes > 0)
        {
            _reportProgress(new IndexerScanProgressEventArgs(root, state.Processed, isCompleted: true));
        }
        return state.FilesOnDisk.Count;
    }

    [LoggerMessage(EventId = 112, Level = LogLevel.Debug, Message = "Stapel geschrieben ({BatchCount} Änderung(en), {TotalCount} Datei(en) gelesen) für Wurzel {Root}.")]
    private static partial void LogBatchCommitted(ILogger logger, int batchCount, int totalCount, string root);

    [LoggerMessage(EventId = 116, Level = LogLevel.Warning, Message = "Wurzel {Root} nur teilweise gelesen — es werden keine Einträge entfernt ({Found} Datei(en) gefunden).")]
    private static partial void LogCleanupSkipped(ILogger logger, string root, int found);

    [LoggerMessage(EventId = 117, Level = LogLevel.Warning, Message = "Stapel mit {BatchCount} Änderung(en) für Wurzel {Root} nicht geschrieben — nächster Abgleich versucht es erneut.")]
    private static partial void LogBatchCommitFailed(ILogger logger, int batchCount, string root, Exception exception);

    [LoggerMessage(EventId = 118, Level = LogLevel.Warning, Message = "Aufzählung der Wurzel {Root} abgebrochen.")]
    private static partial void LogEnumerationFailed(ILogger logger, string root, Exception exception);

    [LoggerMessage(EventId = 119, Level = LogLevel.Information, Message = "{Count} verwaiste Eintrag/Einträge für Wurzel {Root} entfernt.")]
    private static partial void LogTombstonesRemoved(ILogger logger, int count, string root);

    private async Task<Dictionary<string, MarkdownFile>> LoadStoredAsync(string root, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IMarkdownFileRepository repository = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
            IReadOnlyList<MarkdownFile> entries = await repository.GetAllUnderRootAsync(root, cancellationToken).ConfigureAwait(false);
            Dictionary<string, MarkdownFile> byPath = new(entries.Count, PathComparer);
            foreach (MarkdownFile entry in entries)
            {
                _ = byPath.TryAdd(entry.AbsolutePath, entry);
            }
            return byPath;
        }
    }

    private async Task ScanAsync(string root, Dictionary<string, MarkdownFile> stored, ScanState state, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        try
        {
            IMarkdownFileRepository repository = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
            foreach (FileEntry file in _scanner.EnumerateMarkdownFiles(root, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await VisitAsync(repository, root, file, stored, state, cancellationToken).ConfigureAwait(false);
                if (state.PendingInBatch >= Math.Max(1, _options.InitialScanBatchSize))
                {
                    await CommitAsync(repository, root, state, cancellationToken).ConfigureAwait(false);

                    // Ein neuer Scope hält den Change-Tracker klein. Bei zehntausend Dateien und mehr
                    // zöge ein einziger Tracker überproportional viel Speicher.
                    await scope.DisposeAsync().ConfigureAwait(false);
                    scope = _scopeFactory.CreateAsyncScope();
                    repository = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
                }
            }
            state.EnumerationCompleted = true;
            await CommitAsync(repository, root, state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nur die Aufzählung selbst ist abgebrochen. Was schon geschrieben wurde, bleibt stehen;
            // entfernt wird nichts.
            LogEnumerationFailed(_logger, root, ex);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task VisitAsync(
        IMarkdownFileRepository repository,
        string root,
        FileEntry file,
        Dictionary<string, MarkdownFile> stored,
        ScanState state,
        CancellationToken cancellationToken)
    {
        // Größe und Zeit stammen aus der Aufzählung des Ordners. Verschwindet die Datei bis zum
        // Hashen, scheitert erst das Lesen, und der Eintrag bleibt, bis der nächste Abgleich sie
        // nicht mehr findet.
        _ = state.FilesOnDisk.Add(file.Path);
        state.Processed++;
        _ = stored.TryGetValue(file.Path, out MarkdownFile? existing);
        FileStamp stamp = new(file.SizeBytes, file.LastWriteTimeUtc);
        (IndexChange change, _) = await _writer.UpsertAsync(repository, file.Path, root, existing, stamp, cancellationToken).ConfigureAwait(false);
        if (change != IndexChange.None)
        {
            state.Changes++;
            state.PendingInBatch++;
        }
    }

    /// <summary>Schreibt einen Stapel; ein Fehlschlag kostet den Stapel, nicht den Durchgang.</summary>
    /// <remarks>
    /// Die Dateien stehen weiter auf der Platte und werden beim nächsten Abgleich erneut angeboten.
    /// Ohne diesen Schutz risse eine einzelne Datenbank-Spitze den gesamten Dienst mit.
    /// </remarks>
    private async Task CommitAsync(IMarkdownFileRepository repository, string root, ScanState state, CancellationToken cancellationToken)
    {
        if (state.PendingInBatch == 0)
        {
            return;
        }

        try
        {
            _ = await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            LogBatchCommitted(_logger, state.PendingInBatch, state.Processed, root);
            if (state.Mode == SyncMode.Initial || state.Changes > 0)
            {
                _reportProgress(new IndexerScanProgressEventArgs(root, state.Processed, isCompleted: false));
            }
        }
        catch (DbException ex)
        {
            LogBatchCommitFailed(_logger, state.PendingInBatch, root, ex);
        }
        state.PendingInBatch = 0;
    }

    private async Task RemoveVanishedAsync(string root, Dictionary<string, MarkdownFile> stored, ScanState state, CancellationToken cancellationToken)
    {
        List<Guid> vanished = [.. stored.Values.Where(entry => !state.FilesOnDisk.Contains(entry.AbsolutePath)).Select(entry => entry.Id)];
        if (vanished.Count == 0)
        {
            return;
        }

        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IMarkdownFileRepository repository = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();

            // Als Menge und nicht Stück für Stück: Bei einem gewachsenen Bestand sind das Zehntausende
            // Einträge, und einzeln kostet jeder einen Abgleich über alle vorgemerkten.
            int removed = await repository.RemoveRangeAsync(vanished, cancellationToken).ConfigureAwait(false);
            state.Changes += removed;
            LogTombstonesRemoved(_logger, removed, root);
        }
    }

    /// <summary>Zwischenstand eines Durchgangs über eine Wurzel.</summary>
    private sealed class ScanState(SyncMode mode)
    {
        public SyncMode Mode { get; } = mode;

        public HashSet<string> FilesOnDisk { get; } = new(PathComparer);

        public int Processed { get; set; }

        public int Changes { get; set; }

        public int PendingInBatch { get; set; }

        public bool EnumerationCompleted { get; set; }
    }
}
