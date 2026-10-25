using System.Collections.Concurrent;
using MdExplorer.Core.Abstractions;
using MdExplorer.Indexer.Abstractions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Indexer.Services;

/// <summary>
/// Glob-basierte Implementierung von <see cref="IExclusionFilter"/>. Liest die
/// globalen Ausschluss-Muster aus <see cref="ISettingsService"/> und kombiniert sie
/// mit den hierarchischen <c>.mdignore</c>-Mustern unterhalb des jeweiligen Roots.
/// Die Regeln werden pro Verzeichnis lazy aufgebaut und gecacht. <see cref="Invalidate"/> leert den
/// Cache, nach einer Settings-Änderung und wenn der Watcher eine geänderte <c>.mdignore</c> meldet.
/// </summary>
/// <remarks>
/// Muster aus den Einstellungen gelten relativ zur Wurzel, Muster aus einer <c>.mdignore</c>
/// relativ zu dem Ordner, in dem sie liegt, wie in einer <c>.gitignore</c>. Ein <c>!</c>-Muster
/// nimmt eine Datei wieder auf, gleich aus welcher Quelle der Ausschluss stammt.
/// </remarks>
public sealed partial class GlobExclusionFilter : IExclusionFilter, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly MdIgnoreHierarchy _hierarchy;
    private readonly ILogger<GlobExclusionFilter> _logger;
    private readonly ConcurrentDictionary<DirectoryKey, IReadOnlyList<PatternScope>> _scopes = new();
    private volatile UiExcludedFolders? _uiExcluded;
    private bool _disposed;

    /// <summary>Erzeugt den Filter und abonniert <see cref="ISettingsService.SettingsChanged"/>.</summary>
    public GlobExclusionFilter(
        ISettingsService settings,
        MdIgnoreHierarchy hierarchy,
        ILogger<GlobExclusionFilter> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(hierarchy);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _hierarchy = hierarchy;
        _logger = logger;
        _settings.SettingsChanged += OnSettingsChanged;
    }

    /// <inheritdoc />
    public bool IsExcluded(string absoluteFilePath, string rootAbsolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootAbsolutePath);

        string normalizedFile = Path.GetFullPath(absoluteFilePath);
        string normalizedRoot = PathScope.TrimSeparators(Path.GetFullPath(rootAbsolutePath));
        // Datei muss zwingend unterhalb des Roots liegen, sonst ist keine Aussage möglich.
        // Präfix-Vergleich ohne Separator-Terminator würde "C:\Roots-evil" als unter "C:\Roots" zählen.
        if (!PathScope.IsBelow(normalizedFile, normalizedRoot))
        {
            return false;
        }

        // In der Oberfläche pausierte Ordner wirken über den Pfad-Präfix, unabhängig vom Glob-Matcher.
        // Sie stehen vorn, weil ein Präfix-Treffer den teureren Matcher erspart.
        if (IsBelowUiExcludedFolder(normalizedFile))
        {
            return true;
        }

        string? directory = Path.GetDirectoryName(normalizedFile);
        return directory is not null && MatchesPatterns(normalizedRoot, directory, normalizedFile);
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        _uiExcluded = null;
        _scopes.Clear();
        _hierarchy.Clear();
        LogInvalidated(_logger);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _settings.SettingsChanged -= OnSettingsChanged;
    }

    // Ein „!" vorn nimmt Dateien wieder herein, wie in einer .gitignore. Ausschluss und
    // Wiederaufnahme stehen deshalb in getrennten Matchern: Die Wiederaufnahme muss auch einen
    // Ausschluss aus einer anderen Quelle aufheben, und jede Quelle hat ihren eigenen Bezugsordner.
    private static PatternScope BuildScope(string baseDirectory, IReadOnlyList<string> patterns)
    {
        Matcher exclusions = new(StringComparison.OrdinalIgnoreCase);
        Matcher inclusions = new(StringComparison.OrdinalIgnoreCase);
        foreach (string pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }
            if (pattern[0] != '!')
            {
                _ = exclusions.AddInclude(pattern);
                continue;
            }
            string positive = pattern[1..].TrimStart();
            if (positive.Length > 0)
            {
                _ = inclusions.AddInclude(positive);
            }
        }
        return new PatternScope(baseDirectory, exclusions, inclusions);
    }

    [LoggerMessage(EventId = 700, Level = LogLevel.Debug, Message = "Glob-Exclusion-Filter invalidiert (Cache geleert).")]
    private static partial void LogInvalidated(ILogger logger);

    private bool MatchesPatterns(string root, string directory, string file)
    {
        IReadOnlyList<PatternScope> scopes = GetOrBuildScopes(root, directory);
        bool excluded = scopes.Any(scope => scope.Exclusions.Match(scope.RelativePathOf(file)).HasMatches);
        return excluded && !scopes.Any(scope => scope.Inclusions.Match(scope.RelativePathOf(file)).HasMatches);
    }

    private bool IsBelowUiExcludedFolder(string normalizedAbsoluteFile)
    {
        IReadOnlyList<string> configured = _settings.Current.Indexing.UiExcludedFolders;

        // Die Ordner werden einmal je Einstellungsstand normalisiert, nicht je Datei. Neue
        // Einstellungen bringen eine neue Liste mit; daran erkennt der Cache, dass er veraltet ist,
        // auch wenn die Benachrichtigung noch unterwegs ist.
        UiExcludedFolders? cached = _uiExcluded;
        if (cached is null || !ReferenceEquals(cached.Source, configured))
        {
            string[] normalized =
            [
                .. configured
                    .Where(folder => !string.IsNullOrWhiteSpace(folder))
                    .Select(folder => PathScope.TrimSeparators(Path.GetFullPath(folder))),
            ];
            cached = new UiExcludedFolders(configured, normalized);
            _uiExcluded = cached;
        }
        return cached.Normalized.Any(folder => PathScope.IsBelow(normalizedAbsoluteFile, folder));
    }

    private IReadOnlyList<PatternScope> GetOrBuildScopes(string root, string directory)
    {
        DirectoryKey key = new(root, directory);
        return _scopes.GetOrAdd(key, BuildScopes);
    }

    private List<PatternScope> BuildScopes(DirectoryKey key)
    {
        List<PatternScope> scopes = [BuildScope(key.Root, _settings.Current.Indexing.ExclusionPatterns)];
        foreach (MdIgnoreSource source in _hierarchy.GetSourcesFor(key.Root, key.Directory))
        {
            scopes.Add(BuildScope(source.Directory, source.Patterns));
        }
        return scopes;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args) => Invalidate();

    /// <summary>
    /// Cache-Schlüssel der Regeln. Jede Verzeichnisebene bringt ihre eigenen <c>.mdignore</c>-Muster mit,
    /// und die Kette hängt von der Wurzel ab, deshalb gehören beide in den Schlüssel.
    /// </summary>
    private readonly record struct DirectoryKey(string Root, string Directory);

    /// <summary>Die pausierten Ordner eines Einstellungsstands, einmal normalisiert.</summary>
    private sealed record UiExcludedFolders(IReadOnlyList<string> Source, IReadOnlyList<string> Normalized);

    /// <summary>Die Muster einer Quelle mit dem Ordner, auf den sie sich beziehen.</summary>
    private sealed record PatternScope(string BaseDirectory, Matcher Exclusions, Matcher Inclusions)
    {
        public string RelativePathOf(string absoluteFile) =>
            Path.GetRelativePath(BaseDirectory, absoluteFile).Replace('\\', '/');
    }
}
