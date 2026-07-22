using System.IO;
using MdExplorer.Core.Abstractions;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>Minimaler Fake — deckt nur die Aufrufe ab, die der Folder-Tree benötigt.</summary>
internal sealed class FakeFileSystem : IFileSystem
{
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool DirectoryExists(string path) => Directories.Contains(path);

    public bool FileExists(string path) => Files.ContainsKey(path);

    public void EnsureDirectoryExists(string path) => _ = Directories.Add(path);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) => [];

    public IEnumerable<string> EnumerateDirectories(string directory) => [];

    public bool IsReparsePoint(string path) => false;

    public string GetDirectoryFinalPath(string path) => path;

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(Files.TryGetValue(path, out byte[]? bytes) ? bytes : Array.Empty<byte>());

    public byte[] ReadAllBytes(string path) =>
        Files.TryGetValue(path, out byte[]? bytes) ? bytes : Array.Empty<byte>();

    public Stream OpenRead(string path) => Stream.Null;

    public DateTime GetLastWriteTimeUtc(string path) => DateTime.UnixEpoch;

    public long GetFileSize(string path) => 0;

    public Dictionary<string, byte[]> WrittenFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        WrittenFiles[path] = content.ToArray();
        return Task.CompletedTask;
    }
}
