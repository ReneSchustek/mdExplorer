using System.IO;
using System.Text;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Models;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Models;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>Auflösung von WikiLink, Pfad und Kennung aus vorgegebenen Tabellen.</summary>
internal sealed class TableDocumentLocator : IDocumentLocator
{
    private readonly Dictionary<string, Guid> _wikiLinks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid> _absolutePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, string> _idToPath = [];

    /// <summary>Fehler, den die Pfad-Auflösung statt eines Ergebnisses liefert, etwa eine belegte Datenbank.</summary>
    public Exception? FailOnAbsolutePath { get; set; }

    /// <summary>Zählt die Pfad-Auflösungen; jede kostet in der Anwendung eine Datenbankabfrage.</summary>
    public int AbsolutePathCallCount { get; private set; }

    public void SetWikiLink(string target, Guid id) => _wikiLinks[target] = id;

    public void SetAbsolutePath(string absolutePath, Guid id)
    {
        _absolutePaths[absolutePath] = id;
        _idToPath[id] = absolutePath;
    }

    public Task<Guid?> FindByWikiLinkAsync(string wikiLinkTarget, CancellationToken cancellationToken) =>
        Task.FromResult<Guid?>(_wikiLinks.TryGetValue(wikiLinkTarget, out Guid id) ? id : null);

    public Task<Guid?> FindByAbsolutePathAsync(string absoluteFilePath, CancellationToken cancellationToken)
    {
        AbsolutePathCallCount++;
        return FailOnAbsolutePath is not null
            ? Task.FromException<Guid?>(FailOnAbsolutePath)
            : Task.FromResult<Guid?>(_absolutePaths.TryGetValue(absoluteFilePath, out Guid id) ? id : null);
    }

    public Task<string?> GetAbsolutePathAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(_idToPath.TryGetValue(markdownFileId, out string? path) ? path : null);
}

/// <summary>Betriebsanzeige, deren Zustand der Test setzt.</summary>
internal sealed class ScriptedHealthProvider : IOperationHealthProvider
{
    public OperationHealth Current { get; private set; } = OperationHealth.Healthy;

    public string Detail { get; private set; } = "bereit";

    public event EventHandler? Changed;

    public void SetState(OperationHealth state, string detail)
    {
        Current = state;
        Detail = detail;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// UI-Thread zum Mitschreiben: <see cref="Invoke"/> läuft sofort, <see cref="Post"/> erst, wenn
/// der Test die Warteschlange abarbeitet.
/// </summary>
/// <remarks>
/// So wie der echte Dispatcher: Was hinter die laufende Arbeit gestellt wird, ist danach noch nicht
/// passiert. Ein Test sieht damit, ob eine Meldung aus dem Hintergrund den UI-Thread anhält oder
/// sich anstellt.
/// </remarks>
internal sealed class QueuedUiDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _pending = new();

    public int InvokeCount { get; private set; }

    public int PendingCount => _pending.Count;

    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        InvokeCount++;
        action();
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _pending.Enqueue(action);
    }

    /// <summary>Führt alles aus, was sich angestellt hat, auch was dabei neu hinzukommt.</summary>
    public void RunPending()
    {
        while (_pending.TryDequeue(out Action? action))
        {
            action();
        }
    }
}

/// <summary>Dateibestand, den der Test füllt.</summary>
internal sealed class ListAllFilesQuery : IAllFilesQuery
{
    public List<AllFilesRow> Rows { get; } = [];

    public Task<IReadOnlyList<AllFilesRow>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AllFilesRow>>([.. Rows]);
}

/// <summary>Schlagwortstatistik ohne Einträge.</summary>
internal sealed class EmptyTagStatistics : ITagStatisticsService
{
    public Task<IReadOnlyList<TagStatistic>> GetTopTagsAsync(int topN, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TagStatistic>>([]);
}

/// <summary>Parser, der für jeden Text ein leeres Ergebnis liefert.</summary>
internal sealed class EmptyMarkdownParser : IMarkdownParser
{
    public string EngineVersion => "test-engine/1";

    public ParseResult Parse(string markdownText) =>
        new(
            new Dictionary<string, string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            ReadOnlyMemory<byte>.Empty);
}

/// <summary>Gerenderte Dokumente, wie sie der Index für die Vorschau hält.</summary>
internal static class RenderedDocuments
{
    private static readonly Guid DocumentId = new("dddddddd-0000-0000-0000-000000000001");

    public static MarkdownDocument Create(Guid fileId, string body, DateTime parsedAtUtc)
    {
        MarkdownDocument document = new()
        {
            Id = DocumentId,
            MarkdownFileId = fileId,
            SourceContentHash = "hash",
            FrontmatterJson = "{}",
            OutlinksJson = "[]",
            ParsedAtUtc = parsedAtUtc,
        };
        document.SetRenderedHtmlGz(TestGzip.Compress(body));
        return document;
    }
}
