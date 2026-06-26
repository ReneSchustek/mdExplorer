using System.Text.Json;
using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MdExplorer.Search.Services;

/// <summary>
/// Synonym-Quelle, die aus einer JSON-Datei lädt (<see cref="SearchOptions.SynonymFilePath"/>).
/// Schema: <c>Dictionary&lt;string, string[]&gt;</c> mit Lemma → Synonym-Liste.
/// Fehlende Datei und Parse-Fehler werden als leere Map behandelt (Warnung im Log,
/// kein Crash). Die Map wird beim ersten Zugriff lazy geladen und bleibt für die
/// Lebenszeit der Singleton-Instanz im Speicher.
/// </summary>
public sealed partial class FileSynonymProvider : ISynonymProvider
{
    private readonly IFileSystem _fileSystem;
    private readonly SearchOptions _options;
    private readonly ILogger<FileSynonymProvider> _logger;
    private readonly Lock _loadGate = new();
    private Dictionary<string, IReadOnlyList<string>>? _map;

    /// <summary>Erzeugt den Provider und löst Abhängigkeiten auf.</summary>
    public FileSynonymProvider(
        IFileSystem fileSystem,
        IOptions<SearchOptions> options,
        ILogger<FileSynonymProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetSynonyms(string lemma)
    {
        ArgumentNullException.ThrowIfNull(lemma);
        Dictionary<string, IReadOnlyList<string>> map = EnsureLoaded();
        return map.TryGetValue(lemma, out IReadOnlyList<string>? hits)
            ? hits
            : [];
    }

    private Dictionary<string, IReadOnlyList<string>> EnsureLoaded()
    {
        Dictionary<string, IReadOnlyList<string>>? snapshot = _map;
        if (snapshot is not null)
        {
            return snapshot;
        }
        lock (_loadGate)
        {
            snapshot = _map;
            if (snapshot is null)
            {
                snapshot = LoadFromFile();
                _map = snapshot;
            }
            return snapshot;
        }
    }

    private Dictionary<string, IReadOnlyList<string>> LoadFromFile()
    {
        string? path = _options.SynonymFilePath;
        if (string.IsNullOrWhiteSpace(path) || !_fileSystem.FileExists(path))
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            byte[] bytes = _fileSystem.ReadAllBytes(path);
            Dictionary<string, string[]>? raw = JsonSerializer.Deserialize<Dictionary<string, string[]>>(bytes);
            if (raw is null)
            {
                return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            }
            Dictionary<string, IReadOnlyList<string>> normalized = new(raw.Count, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string[]> entry in raw)
            {
                normalized[entry.Key] = entry.Value;
            }
            return normalized;
        }
        catch (JsonException ex)
        {
            LogSynonymParseFailure(_logger, path, ex);
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException ex)
        {
            LogSynonymParseFailure(_logger, path, ex);
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    [LoggerMessage(EventId = 400, Level = LogLevel.Warning, Message = "Synonym-Datei {Path} konnte nicht geladen werden — Similarity läuft ohne Erweiterung.")]
    private static partial void LogSynonymParseFailure(ILogger logger, string path, Exception exception);
}
