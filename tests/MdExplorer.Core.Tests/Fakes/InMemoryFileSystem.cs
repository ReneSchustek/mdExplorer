using MdExplorer.Core.Abstractions;

namespace MdExplorer.Core.Tests.Fakes;

/// <summary>
/// Dateisystem im Speicher für die Core-Tests. Pfade vergleicht es wie Windows ohne
/// Groß-/Kleinschreibung; einzelne Pfade lassen sich gezielt scheitern lassen.
/// </summary>
/// <remarks>
/// Eine gemeinsame Attrappe statt einer je Testklasse: Ändert sich <see cref="IFileSystem"/>,
/// bricht sie an einer Stelle, und alle Tests prüfen gegen dasselbe Verhalten.
/// </remarks>
internal sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _readFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _writeFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _deleteFailures = new(StringComparer.OrdinalIgnoreCase);
    private Task? _heldWrite;
    private TaskCompletionSource? _heldWriteEntered;

    /// <summary>Anzahl atomarer Schreibvorgänge.</summary>
    public int AtomicWrites { get; private set; }

    /// <summary>Pfade aller abgelegten Dateien.</summary>
    public IReadOnlyCollection<string> FilePaths => _files.Keys;

    public void AddFile(string path, byte[] content)
    {
        _files[path] = content;
        AddDirectoryChain(Path.GetDirectoryName(path));
    }

    public void AddFile(string path, string utf8Content) => AddFile(path, System.Text.Encoding.UTF8.GetBytes(utf8Content));

    public void AddDirectory(string path) => AddDirectoryChain(path);

    public byte[] Content(string path) => _files[path];

    public string Text(string path) => System.Text.Encoding.UTF8.GetString(_files[path]);

    public void FailReadsOf(string path, Exception exception) => _readFailures[path] = exception;

    public void FailWritesOf(string path, Exception exception) => _writeFailures[path] = exception;

    public void FailDeletesOf(string path, Exception exception) => _deleteFailures[path] = exception;

    public bool DirectoryExists(string path) => _directories.Contains(path);

    public bool FileExists(string path) => _files.ContainsKey(path);

    public void EnsureDirectoryExists(string path) => AddDirectoryChain(path);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive)
    {
        if (!_directories.Contains(directory))
        {
            throw new DirectoryNotFoundException(directory);
        }
        return [.. _files.Keys.Where(path => IsInside(path, directory, recursive) && Matches(Path.GetFileName(path), searchPattern))];
    }

    public IEnumerable<string> EnumerateDirectories(string directory) =>
        [.. _directories.Where(path => string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase))];

    public bool IsReparsePoint(string path) => false;

    public string GetDirectoryFinalPath(string path) => path;

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) => Task.FromResult(ReadAllBytes(path));

    public byte[] ReadAllBytes(string path)
    {
        if (_readFailures.TryGetValue(path, out Exception? failure))
        {
            throw failure;
        }
        return _files.TryGetValue(path, out byte[]? content) ? content : throw new FileNotFoundException(path);
    }

    public Stream OpenRead(string path) => new MemoryStream(ReadAllBytes(path), writable: false);

    public DateTime GetLastWriteTimeUtc(string path) => FileExists(path) ? DateTime.UnixEpoch : throw new FileNotFoundException(path);

    public long GetFileSize(string path) => ReadAllBytes(path).LongLength;

    /// <summary>
    /// Lässt den nächsten atomaren Schreibvorgang warten, bis <paramref name="release"/> abgeschlossen
    /// ist. <paramref name="entered"/> meldet, dass der Schreibvorgang begonnen hat.
    /// </summary>
    public void HoldNextWrite(Task release, TaskCompletionSource entered)
    {
        _heldWrite = release;
        _heldWriteEntered = entered;
    }

    /// <summary>Wie <see cref="WriteAllBytesAtomicAsync"/>, synchron.</summary>
    public void WriteAllBytesAtomic(string path, ReadOnlySpan<byte> content) =>
        WriteAllBytesAtomicAsync(path, content.ToArray(), CancellationToken.None).GetAwaiter().GetResult();

    public async Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_heldWrite is { } hold)
        {
            _heldWrite = null;
            _ = _heldWriteEntered?.TrySetResult();
            await hold.ConfigureAwait(false);
        }
        if (_writeFailures.TryGetValue(path, out Exception? failure))
        {
            throw failure;
        }
        AtomicWrites++;
        AddFile(path, content.ToArray());
    }

    public void MoveFile(string sourcePath, string destinationPath)
    {
        if (_files.ContainsKey(destinationPath))
        {
            throw new IOException($"Ziel existiert bereits: {destinationPath}");
        }
        AddFile(destinationPath, ReadAllBytes(sourcePath));
        _ = _files.Remove(sourcePath);
    }

    public void DeleteFile(string path)
    {
        if (_deleteFailures.TryGetValue(path, out Exception? failure))
        {
            throw failure;
        }
        _ = _files.Remove(path);
    }

    private static bool IsInside(string path, string directory, bool recursive)
    {
        string? parent = Path.GetDirectoryName(path);
        return recursive
            ? parent is not null && parent.StartsWith(directory, StringComparison.OrdinalIgnoreCase)
            : string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase);
    }

    // Genug für die Muster der Tests: ein Stern steht für beliebig viele Zeichen.
    private static bool Matches(string fileName, string pattern) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            fileName,
            "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));

    private void AddDirectoryChain(string? directory)
    {
        while (!string.IsNullOrEmpty(directory) && _directories.Add(directory))
        {
            directory = Path.GetDirectoryName(directory);
        }
    }
}
