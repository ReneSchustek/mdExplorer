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
/// Matcher werden pro Verzeichnis lazy aufgebaut und gecacht; bei Settings-Änderungen
/// invalidiert der Service alles via <see cref="Invalidate"/>.
/// </summary>
public sealed partial class GlobExclusionFilter : IExclusionFilter, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly MdIgnoreHierarchy _hierarchy;
    private readonly ILogger<GlobExclusionFilter> _logger;
    private readonly ConcurrentDictionary<DirectoryKey, Matcher> _matchers = new();
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
        string normalizedRoot = Path.GetFullPath(rootAbsolutePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Datei muss zwingend unterhalb des Roots liegen — sonst keine Aussage moeglich.
        // Praefix-Vergleich ohne Separator-Terminator wuerde "C:\Roots-evil" als unter "C:\Roots" zaehlen.
        if (!IsUnderRoot(normalizedFile, normalizedRoot))
        {
            return false;
        }

        // UI-Pausen wirken ueber Pfad-Praefix und damit unabhaengig vom Glob-Matcher.
        // Bewusst vor dem Globbing-Pfad, weil ein Praefix-Treffer den teureren Matcher uebergeht.
        if (IsBelowUiExcludedFolder(normalizedFile))
        {
            return true;
        }

        string? directory = Path.GetDirectoryName(normalizedFile);
        if (directory is null)
        {
            return false;
        }

        Matcher matcher = GetOrBuildMatcher(normalizedRoot, directory);
        string relative = Path.GetRelativePath(normalizedRoot, normalizedFile).Replace('\\', '/');
        PatternMatchingResult result = matcher.Match(relative);
        return result.HasMatches;
    }

    private bool IsBelowUiExcludedFolder(string normalizedAbsoluteFile)
    {
        IReadOnlyList<string> uiExcluded = _settings.Current.Indexing.UiExcludedFolders;
        if (uiExcluded.Count == 0)
        {
            return false;
        }
        foreach (string folder in uiExcluded)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }
            string normalizedFolder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (IsUnderRoot(normalizedAbsoluteFile, normalizedFolder))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsUnderRoot(string fileOrDirectory, string root)
    {
        if (fileOrDirectory.Length <= root.Length)
        {
            return false;
        }
        if (!fileOrDirectory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        char delimiter = fileOrDirectory[root.Length];
        return delimiter == Path.DirectorySeparatorChar || delimiter == Path.AltDirectorySeparatorChar;
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        _matchers.Clear();
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

    private Matcher GetOrBuildMatcher(string root, string directory)
    {
        DirectoryKey key = new(root, directory);
        return _matchers.GetOrAdd(key, BuildMatcher);
    }

    private Matcher BuildMatcher(DirectoryKey key)
    {
        Matcher matcher = new(StringComparison.OrdinalIgnoreCase);
        AddPatterns(matcher, _settings.Current.Indexing.ExclusionPatterns);
        IReadOnlyList<string> hierarchical = _hierarchy.GetPatternsFor(key.Root, key.Directory);
        AddPatterns(matcher, hierarchical);
        return matcher;
    }

    private static void AddPatterns(Matcher matcher, IReadOnlyList<string> patterns)
    {
        foreach (string pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }
            if (pattern[0] == '!')
            {
                string positive = pattern[1..].TrimStart();
                if (positive.Length > 0)
                {
                    _ = matcher.AddExclude(positive);
                }
                continue;
            }
            _ = matcher.AddInclude(pattern);
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args) => Invalidate();

    [LoggerMessage(EventId = 700, Level = LogLevel.Debug, Message = "Glob-Exclusion-Filter invalidiert (Cache geleert).")]
    private static partial void LogInvalidated(ILogger logger);

    private readonly record struct DirectoryKey(string Root, string Directory);
}
