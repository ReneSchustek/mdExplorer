using System.Text;
using MdExplorer.Core.Abstractions;

namespace MdExplorer.Indexer.Tests.Fakes;

/// <summary>
/// Dateisystem im Arbeitsspeicher für die Indexer-Tests. Es bildet auch Verknüpfungen, gesperrte
/// Dateien, verschwindende Dateien und eine mitten im Lauf abbrechende Aufzählung nach.
/// </summary>
/// <remarks>
/// Durch eine Sperre geschützt: Der Test legt Dateien an, während der Hintergrunddienst aufzählt.
/// Eine fehlende Datei meldet es wie das echte Dateisystem mit <see cref="FileNotFoundException"/>.
/// </remarks>
internal sealed class FakeFileSystem : IFileSystem
{
    private static readonly DateTime FixedUtc = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly object _gate = new();
    private readonly Dictionary<string, FileEntry> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _symlinks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _fileReparsePoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _vanishOnOpenRead = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _openReadFailures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Lässt die Aufzählung nach so vielen gelieferten Dateien abbrechen.
    /// </summary>
    /// <remarks>
    /// Bildet den halb gelesenen Baum nach: ein Verzeichnis, auf das der Zugriff mitten im
    /// Lauf verweigert wird. Der Indexer darf danach keine Einträge entfernen; was er
    /// nicht gelesen hat, sähe sonst wie gelöscht aus.
    /// </remarks>
    public int? ThrowAfterEnumerating { get; set; }

    /// <summary>Wie oft eine Datei vollständig gelesen wurde.</summary>
    public int ReadAllBytesCalls { get; private set; }

    public void AddDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_gate)
        {
            _ = _directories.Add(NormalizePath(path));
        }
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
        lock (_gate)
        {
            string normalizedLink = NormalizePath(linkPath);
            _ = _directories.Add(normalizedLink);
            _symlinks[normalizedLink] = NormalizePath(targetPath);
        }
    }

    /// <summary>
    /// Markiert eine Datei als Reparse-Point. Mit <paramref name="linkTarget"/> ist sie eine
    /// Verknüpfung auf dieses Ziel; ohne ist sie ein Reparse-Point ohne Ziel, wie ein
    /// OneDrive-Platzhalter, der an Ort und Stelle liegt.
    /// </summary>
    public void MarkFileAsReparsePoint(string path, string? linkTarget = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_gate)
        {
            string normalized = NormalizePath(path);
            _ = _fileReparsePoints.Add(normalized);
            if (linkTarget is not null)
            {
                _symlinks[normalized] = NormalizePath(linkTarget);
            }
        }
    }

    /// <summary>
    /// Lässt die Datei verschwinden, sobald sie zum Lesen geöffnet wird: der Augenblick, in dem
    /// ein Editor seine Zwischendatei löscht, während der Scan sie gerade gefunden hat.
    /// </summary>
    public void VanishOnOpenRead(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_gate)
        {
            _ = _vanishOnOpenRead.Add(NormalizePath(path));
        }
    }

    public void AddFile(string path, string content, DateTime lastWriteUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        string normalized = NormalizePath(path);
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        lock (_gate)
        {
            _files[normalized] = new FileEntry(bytes, lastWriteUtc);
            string? directory = Path.GetDirectoryName(normalized);
            while (!string.IsNullOrEmpty(directory))
            {
                _ = _directories.Add(directory);
                directory = Path.GetDirectoryName(directory);
            }
        }
    }

    public void RemoveFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_gate)
        {
            _ = _files.Remove(NormalizePath(path));
        }
    }

    public void UpdateFile(string path, string content, DateTime lastWriteUtc) =>
        AddFile(path, content, lastWriteUtc);

    /// <summary>Benennt einen Ordner samt Inhalt um.</summary>
    public void MoveDirectory(string oldPath, string newPath)
    {
        string from = NormalizePath(oldPath);
        string to = NormalizePath(newPath);
        lock (_gate)
        {
            foreach (string file in _files.Keys.Where(key => IsBelow(key, from)).ToList())
            {
                FileEntry entry = _files[file];
                _ = _files.Remove(file);
                _files[to + file[from.Length..]] = entry;
            }
            foreach (string dir in _directories.Where(key => IsSameOrBelow(key, from)).ToList())
            {
                _ = _directories.Remove(dir);
                _ = _directories.Add(to + dir[from.Length..]);
            }
        }
    }

    /// <summary>Löscht einen Ordner samt Inhalt.</summary>
    public void RemoveDirectory(string path)
    {
        string dir = NormalizePath(path);
        lock (_gate)
        {
            foreach (string file in _files.Keys.Where(key => IsBelow(key, dir)).ToList())
            {
                _ = _files.Remove(file);
            }
            _ = _directories.RemoveWhere(key => IsSameOrBelow(key, dir));
        }
    }

    public bool DirectoryExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_gate)
        {
            return _directories.Contains(NormalizePath(path));
        }
    }

    public bool FileExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_gate)
        {
            return _files.ContainsKey(NormalizePath(path));
        }
    }

    public void EnsureDirectoryExists(string path) => AddDirectory(path);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);
        return EnumerateFilesCore(directory, searchPattern, recursive);
    }

    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string normalizedRoot = ResolveSymlinkChain(NormalizePath(directory));

        // Ein Laufwerk („C:\") endet schon auf dem Trenner; ein zweiter träfe keinen Ordner darunter.
        string prefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        List<string> directories;
        lock (_gate)
        {
            directories = [.. _directories];
        }

        return directories.Where(dir => dir.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && dir.Length > prefix.Length
            && !dir[prefix.Length..].Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    public bool IsReparsePoint(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = NormalizePath(path);
        lock (_gate)
        {
            return _symlinks.ContainsKey(normalized) || _fileReparsePoints.Contains(normalized);
        }
    }

    public string GetDirectoryFinalPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ResolveSymlinkChain(NormalizePath(path));
    }

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(ReadAllBytes(path));

    public byte[] ReadAllBytes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_gate)
        {
            ReadAllBytesCalls++;
            return Entry(path).Content;
        }
    }

    /// <summary>
    /// Lässt die nächsten <paramref name="times"/> Lesezugriffe auf <paramref name="path"/>
    /// mit einem E/A-Fehler scheitern. Bildet die kurzzeitige Sperre nach, die entsteht,
    /// wenn ein anderes Programm die Datei gerade schreibt, und der Indexer muss das durch
    /// Wiederholen überbrücken statt die Datei zu verlieren.
    /// </summary>
    public void FailOpenRead(string path, int times)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_gate)
        {
            _openReadFailures[NormalizePath(path)] = times;
        }
    }

    public Stream OpenRead(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = NormalizePath(path);
        lock (_gate)
        {
            if (_openReadFailures.TryGetValue(normalized, out int remaining) && remaining > 0)
            {
                _openReadFailures[normalized] = remaining - 1;
                throw new IOException($"Datei ist belegt: {path}");
            }
            if (_vanishOnOpenRead.Remove(normalized))
            {
                _ = _files.Remove(normalized);
            }
            return new MemoryStream(Entry(path).Content, writable: false);
        }
    }

    public DateTime GetLastWriteTimeUtc(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_gate)
        {
            return Entry(path).LastWriteTimeUtc;
        }
    }

    public long GetFileSize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_gate)
        {
            return Entry(path).Content.LongLength;
        }
    }

    /// <summary>Wie <see cref="WriteAllBytesAtomicAsync"/>, synchron.</summary>
    public void WriteAllBytesAtomic(string path, ReadOnlySpan<byte> content) =>
        WriteAllBytesAtomicAsync(path, content.ToArray(), CancellationToken.None).GetAwaiter().GetResult();

    public Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AddFile(path, Encoding.UTF8.GetString(content.Span), FixedUtc);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void MoveFile(string sourcePath, string destinationPath) =>
        throw new NotSupportedException("Diese Attrappe kennt keine Datei-Operationen.");

    /// <inheritdoc />
    public void DeleteFile(string path) =>
        throw new NotSupportedException("Diese Attrappe kennt keine Datei-Operationen.");

    private static string NormalizePath(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    private static bool IsBelow(string path, string directory) =>
        path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrBelow(string path, string directory) =>
        path.Equals(directory, StringComparison.OrdinalIgnoreCase) || IsBelow(path, directory);

    private IEnumerable<string> EnumerateFilesCore(string directory, string searchPattern, bool recursive)
    {
        string normalizedRoot = ResolveSymlinkChain(NormalizePath(directory));
        string extension = searchPattern.TrimStart('*');
        List<string> paths;
        lock (_gate)
        {
            paths = [.. _files.Keys];
        }

        int delivered = 0;
        foreach (string path in paths)
        {
            if (ThrowAfterEnumerating is { } limit && delivered >= limit)
            {
                throw new IOException("Zugriff auf das Verzeichnis verweigert.");
            }
            if (!IsBelow(path, normalizedRoot) || !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!recursive && !string.Equals(Path.GetDirectoryName(path), normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            delivered++;
            yield return path;
        }
    }

    private string ResolveSymlinkChain(string path)
    {
        lock (_gate)
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
    }

    // Aufrufer halten die Sperre. Eine fehlende Datei meldet das echte Dateisystem so, nicht mit
    // einem Schlüsselfehler; der Indexer muss genau diesen Fall abfangen.
    private FileEntry Entry(string path) =>
        _files.TryGetValue(NormalizePath(path), out FileEntry? entry)
            ? entry
            : throw new FileNotFoundException("Datei nicht gefunden.", path);

    /// <summary>Inhalt und Schreibzeitpunkt einer Datei.</summary>
    private sealed record FileEntry(byte[] Content, DateTime LastWriteTimeUtc);
}
