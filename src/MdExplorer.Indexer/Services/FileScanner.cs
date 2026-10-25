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
/// verknüpfte Ordner und Dateien (Symlinks, NTFS-Junctions) gemäß <see cref="IndexerOptions.FollowSymlinks"/>.
/// Das verhindert Endlosschleifen, Mehrfach-Indizierung und Inhalte von außerhalb der Wurzel.
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
    public IEnumerable<FileEntry> EnumerateMarkdownFiles(string rootAbsolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootAbsolutePath);

        if (!_fileSystem.DirectoryExists(rootAbsolutePath))
        {
            return [];
        }

        return EnumerateInternal(rootAbsolutePath, cancellationToken);
    }

    /// <inheritdoc />
    public bool IsExcluded(string absoluteFilePath, string rootAbsolutePath) =>
        _exclusionFilter.IsExcluded(absoluteFilePath, rootAbsolutePath);

    /// <inheritdoc />
    public void InvalidateExclusions() => _exclusionFilter.Invalidate();

    [LoggerMessage(EventId = 200, Level = LogLevel.Information,
        Message = "Symlink/Junction übersprungen: {Path} → {Target}.")]
    private static partial void LogSymlinkSkipped(ILogger logger, string path, string target);

    [LoggerMessage(EventId = 201, Level = LogLevel.Warning,
        Message = "Symlink-Zyklus erkannt — nicht erneut betreten: {Path} → {Target}.")]
    private static partial void LogSymlinkCycle(ILogger logger, string path, string target);

    [LoggerMessage(EventId = 202, Level = LogLevel.Debug,
        Message = "Datei-Verknüpfung übersprungen: {Path} → {Target}.")]
    private static partial void LogFileLinkSkipped(ILogger logger, string path, string target);

    private IEnumerable<FileEntry> EnumerateInternal(string root, CancellationToken cancellationToken)
    {
        HashSet<string> visitedCanonical = new(StringComparer.OrdinalIgnoreCase);
        _ = visitedCanonical.Add(_fileSystem.GetDirectoryFinalPath(root));
        Queue<string> pending = new();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string current = pending.Dequeue();

            foreach (FileEntry entry in EnumerateMarkdownFilesIn(current, root, cancellationToken))
            {
                yield return entry;
            }

            EnqueueDescendableSubdirectories(current, visitedCanonical, pending, cancellationToken);
        }
    }

    private IEnumerable<FileEntry> EnumerateMarkdownFilesIn(string directory, string root, CancellationToken cancellationToken)
    {
        foreach (FileEntry entry in _fileSystem.EnumerateFileEntries(directory, MarkdownSearchPattern))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_exclusionFilter.IsExcluded(entry.Path, root) && !IsSkippedFileLink(entry))
            {
                yield return entry;
            }
        }
    }

    // Übersprungen wird nur eine Datei, die auf ein anderes Ziel zeigt. Ein Reparse-Point ohne
    // eigenes Ziel bleibt drin: OneDrive legt so jede Datei ab, auch die vor Ort liegenden.
    private bool IsSkippedFileLink(FileEntry entry)
    {
        if (_options.FollowSymlinks || !entry.IsReparsePoint)
        {
            return false;
        }
        string target = _fileSystem.GetDirectoryFinalPath(entry.Path);
        if (string.Equals(target, entry.Path, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        LogFileLinkSkipped(_logger, entry.Path, target);
        return true;
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

        // Auch gewöhnliche Ordner landen in der Menge: Zeigt eine Verknüpfung später auf einen
        // von ihnen, wird er als schon besucht erkannt und nicht doppelt indiziert.
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
}
