using System.Text.Json;
using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MdExplorer.Search.Services;

/// <summary>
/// Synonym-Quelle, die aus einer JSON-Datei lädt (<see cref="SearchOptions.SynonymFilePath"/>).
/// Schema: <c>Dictionary&lt;string, string[]&gt;</c> mit Grundform → Synonym-Liste. Nachgeschlagen
/// wird mit dem Stamm der Anfrage; die Schlüssel der Datei werden deshalb beim Laden gestemmt, und
/// Einträge mit demselben Stamm teilen sich ihre Synonyme.
/// Fehlende Datei und Parse-Fehler werden als leere Map behandelt (Warnung im Log,
/// kein Crash). Die Map wird beim ersten Zugriff lazy geladen und bleibt für die
/// Lebenszeit der Singleton-Instanz im Speicher; eine geänderte Datei wirkt erst nach einem Neustart.
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
    public IReadOnlyList<string> GetSynonyms(string stem)
    {
        ArgumentNullException.ThrowIfNull(stem);
        Dictionary<string, IReadOnlyList<string>> map = EnsureLoaded();
        return map.TryGetValue(stem, out IReadOnlyList<string>? hits)
            ? hits
            : [];
    }

    [LoggerMessage(EventId = 400, Level = LogLevel.Warning, Message = "Synonym-Datei {Path} konnte nicht geladen werden — Similarity läuft ohne Erweiterung.")]
    private static partial void LogSynonymParseFailure(ILogger logger, string path, Exception exception);

    private static Dictionary<string, IReadOnlyList<string>> EmptyMap() =>
        new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, IReadOnlyList<string>> ByStem(Dictionary<string, string[]> raw)
    {
        Dictionary<string, List<string>> merged = new(raw.Count, StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string[]> entry in raw)
        {
            string stem = GermanStemmer.Stem(entry.Key);
            if (!merged.TryGetValue(stem, out List<string>? synonyms))
            {
                synonyms = [];
                merged[stem] = synonyms;
            }

            // Ein Null-Wert (JSON "wagen": null) zählt als leere Liste.
            synonyms.AddRange(entry.Value ?? []);
        }

        Dictionary<string, IReadOnlyList<string>> result = new(merged.Count, StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, List<string>> entry in merged)
        {
            result[entry.Key] = [.. entry.Value.Distinct(StringComparer.OrdinalIgnoreCase)];
        }
        return result;
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
            return EmptyMap();
        }

        try
        {
            byte[] bytes = _fileSystem.ReadAllBytes(path);
            Dictionary<string, string[]>? raw = JsonSerializer.Deserialize<Dictionary<string, string[]>>(bytes);
            if (raw is null)
            {
                return EmptyMap();
            }
            return ByStem(raw);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            // Eine defekte oder gesperrte Synonym-Datei schaltet nur die Erweiterung ab, nicht die Suche.
            LogSynonymParseFailure(_logger, path, ex);
            return EmptyMap();
        }
    }
}
