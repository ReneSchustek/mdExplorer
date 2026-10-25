using System.Text;
using MdExplorer.Core.Abstractions;

namespace MdExplorer.Search.Tests.Fakes;

/// <summary>
/// In-Memory-Dateisystem für die Tests des Such-Moduls. Liefert nur die in <see cref="AddFile"/>
/// hinterlegten Inhalte zurück und schreibt synchrone Lesezugriffe in <see cref="ReadCalls"/> mit;
/// daran hängt der Nachweis des Zwischenspeichers der Synonymdatei.
/// </summary>
internal sealed class FakeFileSystem : IFileSystem
{
    private static readonly DateTime FixedUtc = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    public List<string> ReadCalls { get; } = [];

    public void AddFile(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        _files[path] = Encoding.UTF8.GetBytes(content);
    }

    public bool DirectoryExists(string path) => true;

    public bool FileExists(string path) => _files.ContainsKey(path);

    public void EnsureDirectoryExists(string path)
    {
    }

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) =>
        throw new NotSupportedException("Wird in den Search-Integrationstests nicht benötigt.");

    public IEnumerable<string> EnumerateDirectories(string directory) =>
        throw new NotSupportedException("Wird in den Search-Integrationstests nicht benötigt.");

    public bool IsReparsePoint(string path) => false;

    public string GetDirectoryFinalPath(string path) => path;

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        if (!_files.TryGetValue(path, out byte[]? bytes))
        {
            throw new FileNotFoundException($"Fake-Datei nicht vorhanden: {path}", path);
        }
        return Task.FromResult(bytes);
    }

    public byte[] ReadAllBytes(string path)
    {
        ReadCalls.Add(path);
        if (!_files.TryGetValue(path, out byte[]? bytes))
        {
            throw new FileNotFoundException($"Fake-Datei nicht vorhanden: {path}", path);
        }
        return bytes;
    }

    public Stream OpenRead(string path) => new MemoryStream(_files[path], writable: false);

    public DateTime GetLastWriteTimeUtc(string path) => FixedUtc;

    public long GetFileSize(string path) => _files[path].LongLength;

    /// <summary>Wie <see cref="WriteAllBytesAtomicAsync"/>, synchron.</summary>
    public void WriteAllBytesAtomic(string path, ReadOnlySpan<byte> content) =>
        WriteAllBytesAtomicAsync(path, content.ToArray(), CancellationToken.None).GetAwaiter().GetResult();

    public Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        _files[path] = content.ToArray();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void MoveFile(string sourcePath, string destinationPath) =>
        throw new NotSupportedException("Diese Attrappe kennt keine Datei-Operationen.");

    /// <inheritdoc />
    public void DeleteFile(string path) =>
        throw new NotSupportedException("Diese Attrappe kennt keine Datei-Operationen.");
}
