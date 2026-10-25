using MdExplorer.Core.Abstractions;

namespace MdExplorer.Search.Services;

/// <summary>
/// Merkt sich Quelldateien, die sich zuletzt nicht lesen ließen, mit Inhaltsstand und frühestem
/// neuen Versuch.
/// </summary>
/// <remarks>
/// Nur im Speicher: Nach einem Neustart wird jede Datei einmal sofort neu versucht, und das ist
/// gewollt. Dateien, die es nicht mehr gibt, werden bei jedem Abgleich vergessen.
/// </remarks>
internal sealed class ReadRetryTracker
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, ReadFailure> _failures = [];
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retryDelay;

    /// <summary>Erzeugt die Buchhaltung mit Uhr und Wartezeit bis zum nächsten Versuch.</summary>
    public ReadRetryTracker(TimeProvider timeProvider, TimeSpan retryDelay)
    {
        _timeProvider = timeProvider;
        _retryDelay = retryDelay;
    }

    /// <summary>Ob irgendeine Datei noch auf einen neuen Leseversuch wartet.</summary>
    public bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _failures.Count > 0;
            }
        }
    }

    /// <summary>Ob die Datei mit diesem Inhaltsstand noch auf ihren nächsten Versuch wartet.</summary>
    public bool IsWaiting(SearchSourceDocument document)
    {
        lock (_gate)
        {
            return _failures.TryGetValue(document.MarkdownFileId, out ReadFailure failure)
                && string.Equals(failure.ContentHash, document.SourceContentHash, StringComparison.Ordinal)
                && _timeProvider.GetUtcNow() < failure.RetryAt;
        }
    }

    /// <summary>Die Datei ließ sich lesen; ein früherer Fehler ist vergessen.</summary>
    public void Succeeded(Guid markdownFileId)
    {
        lock (_gate)
        {
            _ = _failures.Remove(markdownFileId);
        }
    }

    /// <summary>Vermerkt einen Lesefehler.</summary>
    /// <returns>Ob derselbe Inhaltsstand schon einmal gescheitert war.</returns>
    public bool Failed(SearchSourceDocument document)
    {
        lock (_gate)
        {
            bool repeated = _failures.TryGetValue(document.MarkdownFileId, out ReadFailure previous)
                && string.Equals(previous.ContentHash, document.SourceContentHash, StringComparison.Ordinal);
            _failures[document.MarkdownFileId] = new ReadFailure(document.SourceContentHash, _timeProvider.GetUtcNow() + _retryDelay);
            return repeated;
        }
    }

    /// <summary>Vergisst die Vermerke von Dateien, die nicht mehr im Bestand stehen.</summary>
    public void ForgetOtherThan(IReadOnlySet<Guid> liveIds)
    {
        lock (_gate)
        {
            foreach (Guid id in _failures.Keys.Where(id => !liveIds.Contains(id)).ToList())
            {
                _ = _failures.Remove(id);
            }
        }
    }

    /// <summary>Ein Lesefehler mit dem Inhaltsstand, an dem er auftrat, und dem frühesten neuen Versuch.</summary>
    private readonly record struct ReadFailure(string ContentHash, DateTimeOffset RetryAt);
}
