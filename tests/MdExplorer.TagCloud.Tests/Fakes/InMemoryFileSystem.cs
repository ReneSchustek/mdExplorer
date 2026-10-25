using System.IO;
using System.Text;
using MdExplorer.Core.Abstractions;

namespace MdExplorer.TagCloud.Tests.Fakes;

/// <summary>Dateisystem im Arbeitsspeicher; Texte gehen als UTF-8 ohne BOM hinein und heraus.</summary>
internal sealed class InMemoryFileSystem : IFileSystem
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _lockedForWriting = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lässt jedes Schreiben dieser Datei scheitern, wie bei einer Sperre durch ein anderes Programm.</summary>
    public void LockForWriting(string path) => _lockedForWriting.Add(path);

    public void AddFile(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _files[path] = Utf8NoBom.GetBytes(content);
    }

    public void AddBytes(string path, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _files[path] = content;
    }

    public string ReadText(string path) => Utf8NoBom.GetString(_files[path]);

    public byte[] ReadBytes(string path) => _files[path];

    public bool DirectoryExists(string path) => false;
    public bool FileExists(string path) => _files.ContainsKey(path);
    public void EnsureDirectoryExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
    }

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) => [];

    public IEnumerable<string> EnumerateDirectories(string directory) => [];

    public bool IsReparsePoint(string path) => false;

    public string GetDirectoryFinalPath(string path) => path;

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(_files[path]);

    public byte[] ReadAllBytes(string path) => _files[path];

    public Stream OpenRead(string path) => new MemoryStream(_files[path], writable: false);

    public DateTime GetLastWriteTimeUtc(string path) => DateTime.UnixEpoch;

    public long GetFileSize(string path) => _files[path].LongLength;

    /// <summary>Wie <see cref="WriteAllBytesAtomicAsync"/>, synchron.</summary>
    public void WriteAllBytesAtomic(string path, ReadOnlySpan<byte> content) =>
        WriteAllBytesAtomicAsync(path, content.ToArray(), CancellationToken.None).GetAwaiter().GetResult();

    public Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_lockedForWriting.Contains(path))
        {
            return Task.FromException(new IOException("Die Datei wird von einem anderen Prozess verwendet."));
        }
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
