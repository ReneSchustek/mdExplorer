using System.Text;
using MdExplorer.Core.Abstractions;

namespace MdExplorer.Indexer.Tests.Fakes;

/// <summary>
/// In-Memory-Datei-System für Indexer-Unit-Tests.
/// </summary>
internal sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, FileEntry> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _symlinks = new(StringComparer.OrdinalIgnoreCase);

    public void AddDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _ = _directories.Add(NormalizePath(path));
    }

    /// <summary>
    /// Markiert <paramref name="linkPath"/> als Reparse-Point, der auf
    /// <paramref name="targetPath"/> zeigt. Verzeichnis-Auflistungen und Datei-Suchen
    /// auf <paramref name="linkPath"/> delegieren transparent an das Ziel.
    /// </summary>
    public void AddSymlink(string linkPath, string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        string normalizedLink = NormalizePath(linkPath);
        string normalizedTarget = NormalizePath(targetPath);
        _ = _directories.Add(normalizedLink);
        _symlinks[normalizedLink] = normalizedTarget;
    }

    public void AddFile(string path, string content, DateTime lastWriteUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        string normalized = NormalizePath(path);
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        _files[normalized] = new FileEntry(bytes, lastWriteUtc);

        string? directory = Path.GetDirectoryName(normalized);
        while (!string.IsNullOrEmpty(directory))
        {
            _ = _directories.Add(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    public void RemoveFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _ = _files.Remove(NormalizePath(path));
    }

    public void UpdateFile(string path, string content, DateTime lastWriteUtc) =>
        AddFile(path, content, lastWriteUtc);

    public bool DirectoryExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _directories.Contains(NormalizePath(path));
    }

    public bool FileExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _files.ContainsKey(NormalizePath(path));
    }

    public void EnsureDirectoryExists(string path) => AddDirectory(path);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);
        string normalizedRoot = ResolveSymlinkChain(NormalizePath(directory));
        string extension = searchPattern.TrimStart('*');

        foreach (string path in _files.Keys)
        {
            if (!path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!recursive)
            {
                string? parent = Path.GetDirectoryName(path);
                if (!string.Equals(parent, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }
            yield return path;
        }
    }

    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string normalizedRoot = ResolveSymlinkChain(NormalizePath(directory));
        string prefix = normalizedRoot + Path.DirectorySeparatorChar;

        foreach (string dir in _directories)
        {
            if (!dir.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string remainder = dir[prefix.Length..];
            if (remainder.Length == 0 || remainder.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }
            yield return dir;
        }
    }

    public bool IsReparsePoint(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _symlinks.ContainsKey(NormalizePath(path));
    }

    public string GetDirectoryFinalPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ResolveSymlinkChain(NormalizePath(path));
    }

    private string ResolveSymlinkChain(string path)
    {
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        string current = path;
        while (_symlinks.TryGetValue(current, out string? next))
        {
            if (!visited.Add(current))
            {
                return current;
            }
            current = next;
        }
        return current;
    }

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.FromResult(_files[NormalizePath(path)].Content);
    }

    public byte[] ReadAllBytes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _files[NormalizePath(path)].Content;
    }

    public Stream OpenRead(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new MemoryStream(_files[NormalizePath(path)].Content, writable: false);
    }

    public DateTime GetLastWriteTimeUtc(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _files[NormalizePath(path)].LastWriteTimeUtc;
    }

    public long GetFileSize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _files[NormalizePath(path)].Content.LongLength;
    }

    public Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AddFile(path, Encoding.UTF8.GetString(content.Span), FixedUtc);
        return Task.CompletedTask;
    }

    private static readonly DateTime FixedUtc = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    private static string NormalizePath(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    private sealed record FileEntry(byte[] Content, DateTime LastWriteTimeUtc);
}
