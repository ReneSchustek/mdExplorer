using System.Text;
using MdExplorer.Core.Abstractions;

namespace MdExplorer.Parser.Tests.Fakes;

/// <summary>
/// In-Memory-Datei-System für Parser-Unit-Tests — angelehnt an die Indexer-Variante,
/// erweitert um <see cref="FailOnRead"/> für Read-Fehler-Tests.
/// </summary>
internal sealed class FakeFileSystem : IFileSystem
{
    private static readonly DateTime FixedUtc = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> FailOnRead { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void AddFile(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        _files[Normalize(path)] = Encoding.UTF8.GetBytes(content);
    }

    public bool DirectoryExists(string path) => true;

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public void EnsureDirectoryExists(string path)
    {
    }

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) =>
        throw new NotSupportedException("Not needed for orchestrator tests.");

    public IEnumerable<string> EnumerateDirectories(string directory) =>
        throw new NotSupportedException("Not needed for orchestrator tests.");

    public bool IsReparsePoint(string path) => false;

    public string GetDirectoryFinalPath(string path) => Normalize(path);

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        string normalized = Normalize(path);
        if (FailOnRead.Contains(normalized))
        {
            throw new IOException($"Simulated read failure for {normalized}.");
        }
        return Task.FromResult(_files[normalized]);
    }

    public byte[] ReadAllBytes(string path)
    {
        string normalized = Normalize(path);
        if (FailOnRead.Contains(normalized))
        {
            throw new IOException($"Simulated read failure for {normalized}.");
        }
        return _files[normalized];
    }

    public Stream OpenRead(string path) => new MemoryStream(_files[Normalize(path)], writable: false);

    public DateTime GetLastWriteTimeUtc(string path) => FixedUtc;

    public long GetFileSize(string path) => _files[Normalize(path)].LongLength;

    public Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        _files[Normalize(path)] = content.ToArray();
        return Task.CompletedTask;
    }

    private static string Normalize(string path) => path.Replace('/', Path.DirectorySeparatorChar);
}
