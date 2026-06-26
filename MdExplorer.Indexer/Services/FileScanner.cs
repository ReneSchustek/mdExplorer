using MdExplorer.Core.Abstractions;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MdExplorer.Indexer.Services;

/// <summary>
/// Rein iterativer Markdown-Scanner, vollständig über <see cref="IFileSystem"/> entkoppelt.
/// Delegiert die Ausschluss-Logik an einen <see cref="IExclusionFilter"/>, der Settings und
/// <c>.mdignore</c>-Hierarchie kombiniert. Steigt verzeichnisweise per BFS ab und überspringt
/// Symlinks / NTFS-Junctions (Reparse-Points) gemäß <see cref="IndexerOptions.FollowSymlinks"/>
/// — verhindert Endlosschleifen und Mehrfach-Indizierung.
/// </summary>
public sealed partial class FileScanner : IFileScanner
{
    private const string MarkdownSearchPattern = "*.md";

    private readonly IFileSystem _fileSystem;
    private readonly IExclusionFilter _exclusionFilter;
    private readonly IndexerOptions _options;
    private readonly ILogger<FileScanner> _logger;

    /// <summary>Erzeugt einen Scanner und löst seine Abhängigkeiten auf.</summary>
    public FileScanner(
        IFileSystem fileSystem,
        IExclusionFilter exclusionFilter,
        IOptions<IndexerOptions> options,
        ILogger<FileScanner> logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(exclusionFilter);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _fileSystem = fileSystem;
        _exclusionFilter = exclusionFilter;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateMarkdownFiles(string rootAbsolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootAbsolutePath);

        if (!_fileSystem.DirectoryExists(rootAbsolutePath))
        {
            return [];
        }

        return EnumerateInternal(rootAbsolutePath, cancellationToken);
    }

    private IEnumerable<string> EnumerateInternal(string root, CancellationToken cancellationToken)
    {
        HashSet<string> visitedCanonical = new(StringComparer.OrdinalIgnoreCase);
        _ = visitedCanonical.Add(_fileSystem.GetDirectoryFinalPath(root));
        Queue<string> pending = new();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string current = pending.Dequeue();

            foreach (string path in EnumerateMarkdownFilesIn(current, root, cancellationToken))
            {
                yield return path;
            }

            EnqueueDescendableSubdirectories(current, visitedCanonical, pending, cancellationToken);
        }
    }

    private IEnumerable<string> EnumerateMarkdownFilesIn(string directory, string root, CancellationToken cancellationToken)
    {
        foreach (string path in _fileSystem.EnumerateFiles(directory, MarkdownSearchPattern, recursive: false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_exclusionFilter.IsExcluded(path, root))
            {
                yield return path;
            }
        }
    }

    private void EnqueueDescendableSubdirectories(
        string current,
        HashSet<string> visitedCanonical,
        Queue<string> pending,
        CancellationToken cancellationToken)
    {
        foreach (string subdir in _fileSystem.EnumerateDirectories(current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ShouldDescendInto(subdir, visitedCanonical))
            {
                pending.Enqueue(subdir);
            }
        }
    }

    private bool ShouldDescendInto(string subdir, HashSet<string> visitedCanonical)
    {
        bool isReparsePoint = _fileSystem.IsReparsePoint(subdir);
        string canonical = _fileSystem.GetDirectoryFinalPath(subdir);
        if (isReparsePoint && !_options.FollowSymlinks)
        {
            LogSymlinkSkipped(_logger, subdir, canonical);
            return false;
        }

        if (!visitedCanonical.Add(canonical))
        {
            if (isReparsePoint)
            {
                LogSymlinkCycle(_logger, subdir, canonical);
            }
            return false;
        }

        return true;
    }

    [LoggerMessage(EventId = 200, Level = LogLevel.Information,
        Message = "Symlink/Junction übersprungen: {Path} → {Target}.")]
    private static partial void LogSymlinkSkipped(ILogger logger, string path, string target);

    [LoggerMessage(EventId = 201, Level = LogLevel.Warning,
        Message = "Symlink-Zyklus erkannt — nicht erneut betreten: {Path} → {Target}.")]
    private static partial void LogSymlinkCycle(ILogger logger, string path, string target);
}
