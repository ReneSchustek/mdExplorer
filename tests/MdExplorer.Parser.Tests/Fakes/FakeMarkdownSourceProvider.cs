using MdExplorer.Core.Abstractions;

namespace MdExplorer.Parser.Tests.Fakes;

/// <summary>
/// Liefert die Quelldateien für den Orchestrator aus einer Liste, zählt die Aufzählungen und
/// kann eine Aufzählung oder jede einzelne mit einer Ausnahme abbrechen lassen.
/// </summary>
/// <remarks>
/// Tests der Hintergrundschleife fügen Dateien hinzu, während die Schleife aufzählt; die Liste
/// ist deshalb gesperrt und jede Aufzählung arbeitet auf einer Kopie.
/// </remarks>
internal sealed class FakeMarkdownSourceProvider : IMarkdownSourceProvider
{
    private readonly List<MarkdownSourceSnapshot> _sources = [];
    private readonly Lock _gate = new();
    private int _calls;

    /// <summary>
    /// Ausnahme, mit der die nächste Aufzählung einmal abbricht; danach steht der Wert wieder auf
    /// <c>null</c>. Damit lässt sich prüfen, dass die Parse-Schleife einen Fehler beim Laden übersteht.
    /// </summary>
    public Exception? ThrowOnNextEnumeration { get; set; }

    /// <summary>Ausnahme, mit der jede Aufzählung abbricht; zeigt, ob die Schleife dauerhaft weiterläuft.</summary>
    public Exception? ThrowOnEveryEnumeration { get; set; }

    /// <summary>Wie oft aufgezählt wurde.</summary>
    public int CallCount => Volatile.Read(ref _calls);

    /// <summary>Eine Kopie der Quellen.</summary>
    public IReadOnlyList<MarkdownSourceSnapshot> Sources
    {
        get
        {
            lock (_gate)
            {
                return [.. _sources];
            }
        }
    }

    public void Add(MarkdownSourceSnapshot snapshot)
    {
        lock (_gate)
        {
            _sources.Add(snapshot);
        }
    }

    public void Replace(Guid id, Func<MarkdownSourceSnapshot, MarkdownSourceSnapshot> change)
    {
        lock (_gate)
        {
            int index = _sources.FindIndex(snapshot => snapshot.Id == id);
            if (index < 0)
            {
                throw new InvalidOperationException($"Source with id {id} not found.");
            }
            _sources[index] = change(_sources[index]);
        }
    }

    public async IAsyncEnumerable<MarkdownSourceSnapshot> EnumerateAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _ = Interlocked.Increment(ref _calls);
        Exception? toThrow = ThrowOnEveryEnumeration ?? ThrowOnNextEnumeration;
        if (toThrow is not null)
        {
            ThrowOnNextEnumeration = null;
            await Task.Yield();
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(toThrow).Throw();
        }
        foreach (MarkdownSourceSnapshot snapshot in Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return snapshot;
            await Task.Yield();
        }
    }
}
