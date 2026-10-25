using MdExplorer.Core.Models;
using MdExplorer.Data;

namespace MdExplorer.Indexer.Tests.Fakes;

/// <summary>
/// Der geschriebene Bestand der Indexer-Tests, geteilt von allen Scopes. Jeder Scope arbeitet über
/// eine eigene <see cref="FakeMarkdownFileRepository"/>-Sitzung mit eigener Vormerkliste, wie ein
/// eigener DbContext: Was eine Sitzung vormerkt und nicht speichert, verfällt mit ihr.
/// </summary>
/// <remarks>
/// Gelesen wird immer eine Kopie. Ändert der Indexer eine geladene Datei, ohne sie über
/// <c>Update</c> vorzumerken, kommt die Änderung wie bei EF nicht im Bestand an. Der Bestand ist
/// durch eine Sperre geschützt, weil Testfaden und Hintergrunddienst gleichzeitig zugreifen.
/// </remarks>
internal sealed class FakeMarkdownFileStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, MarkdownFile> _committed = [];
    private TaskCompletionSource? _saveHold;
    private TaskCompletionSource? _singleSaveHold;
    private bool _singleSaveTaken;

    /// <summary>Jeder Schreibzugriff: ein Speichern, auch ein gescheitertes, oder ein Entfernen als Menge.</summary>
    public EventCounter Writes { get; } = new();

    /// <summary>Jede Abfrage einer ganzen Wurzel, also der Beginn eines Durchgangs.</summary>
    public EventCounter RootQueries { get; } = new();

    /// <summary>Jeder Beginn eines Speicherns, auch eines angehaltenen.</summary>
    public EventCounter SavesEntered { get; } = new();

    public int TotalWrites { get; private set; }

    public int SaveCallCount { get; private set; }

    public int RootQueryCount => (int)RootQueries.Mark;

    /// <summary>Speichervorgänge, die am eindeutigen Pfad scheiterten, weil eine Datei doppelt angelegt werden sollte.</summary>
    public int DuplicateRejectCount { get; private set; }

    /// <summary>Abfragen einzelner Dateien über ihren Pfad.</summary>
    public int PathLookupCount { get; private set; }

    /// <summary>Ausnahme, die der nächste Speichervorgang einmal wirft.</summary>
    public Exception? ThrowOnNextSave { get; set; }

    /// <summary>Ausnahme, die die nächste Abfrage einer Wurzel einmal wirft.</summary>
    public Exception? ThrowOnNextRootQuery { get; set; }

    /// <summary>Die Schlüssel, die als Menge entfernt wurden, je Aufruf eine Liste.</summary>
    public List<IReadOnlyCollection<Guid>> RemovedRanges { get; } = [];

    /// <summary>Aktueller Stand, indiziert nach <c>AbsolutePath</c>.</summary>
    public IReadOnlyDictionary<string, MarkdownFile> Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _committed.Values.ToDictionary(file => file.AbsolutePath, Copy, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public FakeMarkdownFileRepository CreateSession() => new(this);

    /// <summary>Hält jeden folgenden Speichervorgang an, bis <see cref="ReleaseSaves"/> kommt.</summary>
    public void HoldSaves()
    {
        lock (_gate)
        {
            _saveHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>
    /// Hält nur den nächsten Speichervorgang an, bis <see cref="ReleaseSaves"/> kommt; alle
    /// folgenden laufen durch. So kann ein zweiter Schreiber in der Lücke speichern.
    /// </summary>
    public void HoldNextSave()
    {
        lock (_gate)
        {
            _singleSaveHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _singleSaveTaken = false;
        }
    }

    public void ReleaseSaves()
    {
        TaskCompletionSource? hold;
        TaskCompletionSource? single;
        lock (_gate)
        {
            hold = _saveHold;
            single = _singleSaveHold;
            _saveHold = null;
            _singleSaveHold = null;
        }
        _ = hold?.TrySetResult();
        _ = single?.TrySetResult();
    }

    internal Task EnterSaveAsync()
    {
        SavesEntered.Signal();
        lock (_gate)
        {
            if (_saveHold is not null)
            {
                return _saveHold.Task;
            }
            if (_singleSaveHold is { Task.IsCompleted: false } single && !_singleSaveTaken)
            {
                _singleSaveTaken = true;
                return single.Task;
            }
            return Task.CompletedTask;
        }
    }

    internal MarkdownFile? FindByPath(string absolutePath)
    {
        lock (_gate)
        {
            PathLookupCount++;
            MarkdownFile? match = _committed.Values.FirstOrDefault(
                file => string.Equals(file.AbsolutePath, absolutePath, StringComparison.OrdinalIgnoreCase));
            return match is null ? null : Copy(match);
        }
    }

    internal MarkdownFile? FindById(Guid id)
    {
        lock (_gate)
        {
            return _committed.TryGetValue(id, out MarkdownFile? file) ? Copy(file) : null;
        }
    }

    internal IReadOnlyList<MarkdownFile> FindUnder(string directory)
    {
        Exception? toThrow;
        List<MarkdownFile> result;
        lock (_gate)
        {
            toThrow = ThrowOnNextRootQuery;
            ThrowOnNextRootQuery = null;

            // Wie die Datenschicht mit Trenner am Ende: "C:\Notes" trifft nicht "C:\Notes-alt".
            string prefix = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            result = [.. _committed.Values
                .Where(file => file.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(Copy)];
        }
        RootQueries.Signal();
        return toThrow is null ? result : throw toThrow;
    }

    internal int Count()
    {
        lock (_gate)
        {
            return _committed.Count;
        }
    }

    internal int RemoveRange(IReadOnlyCollection<Guid> ids)
    {
        int removed;
        lock (_gate)
        {
            RemovedRanges.Add(ids);
            removed = ids.Count(_committed.Remove);
        }
        Writes.Signal();
        return removed;
    }

    internal int Commit(IReadOnlyList<MarkdownFile> adds, IReadOnlyCollection<MarkdownFile> updates, IReadOnlyList<MarkdownFile> removes)
    {
        try
        {
            lock (_gate)
            {
                SaveCallCount++;
                Exception? toThrow = ThrowOnNextSave;
                ThrowOnNextSave = null;
                if (toThrow is not null)
                {
                    throw toThrow;
                }

                ApplyCommit(adds, updates, removes);
                int writes = adds.Count + updates.Count + removes.Count;
                TotalWrites += writes;
                return writes;
            }
        }
        finally
        {
            Writes.Signal();
        }
    }

    private static MarkdownFile Copy(MarkdownFile file) => new()
    {
        Id = file.Id,
        AbsolutePath = file.AbsolutePath,
        RelativePath = file.RelativePath,
        FileNameWithoutExtension = file.FileNameWithoutExtension,
        SizeBytes = file.SizeBytes,
        LastWriteTimeUtc = file.LastWriteTimeUtc,
        ContentHash = file.ContentHash,
        IndexedAtUtc = file.IndexedAtUtc,
    };

    // Wie die Datenbank mit ihrem eindeutigen Index auf dem Pfad: Zwei Einträge für dieselbe Datei
    // lassen sich nicht speichern. Ohne diese Prüfung bliebe eine Doppelanlage im Test unsichtbar.
    private void ApplyCommit(IReadOnlyList<MarkdownFile> adds, IReadOnlyCollection<MarkdownFile> updates, IReadOnlyList<MarkdownFile> removes)
    {
        Dictionary<Guid, MarkdownFile> result = new(_committed);
        foreach (MarkdownFile remove in removes)
        {
            _ = result.Remove(remove.Id);
        }
        foreach (MarkdownFile entry in adds.Concat(updates))
        {
            result[entry.Id] = Copy(entry);
        }

        if (result.Values.GroupBy(file => file.AbsolutePath, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            DuplicateRejectCount++;
            throw new DatabaseOperationException("UNIQUE constraint failed: MarkdownFiles.AbsolutePath");
        }

        _committed.Clear();
        foreach (KeyValuePair<Guid, MarkdownFile> entry in result)
        {
            _committed[entry.Key] = entry.Value;
        }
    }
}
