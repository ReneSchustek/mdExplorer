using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Settings;
using MdExplorer.Indexer.Services;
using MdExplorer.Indexer.Tests.Fakes;

namespace MdExplorer.Indexer.Tests.Services;

/// <summary>
/// Direkt-Tests für <see cref="MdIgnoreHierarchy"/> — bisher nur indirekt
/// über <c>GlobExclusionFilterTests</c> abgedeckt.
/// </summary>
public sealed class MdIgnoreHierarchyTests
{
    [Fact]
    public void GetPatternsFor_OnDirectoryNotUnderRoot_ReturnsEmpty()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(@"C:\Wurzel");
        MdIgnoreHierarchy sut = NewHierarchy(fs);

        IReadOnlyList<string> result = sut.GetPatternsFor(@"C:\Wurzel", @"C:\Andere\Pfad");

        Assert.Empty(result);
    }

    [Fact]
    public void GetPatternsFor_OnRootIdenticalToDirectory_ReturnsOnlyRootPatterns()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(@"C:\Wurzel");
        fs.AddFile(@"C:\Wurzel\.mdignore", "**/temp/**\n", DateTime.UnixEpoch);
        MdIgnoreHierarchy sut = NewHierarchy(fs);

        IReadOnlyList<string> result = sut.GetPatternsFor(@"C:\Wurzel", @"C:\Wurzel");

        Assert.Equal(["**/temp/**"], result);
    }

    [Fact]
    public void GetPatternsFor_OnDeepDirectory_ReturnsParentChainInOrder()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(@"C:\Wurzel");
        fs.AddDirectory(@"C:\Wurzel\sub");
        fs.AddDirectory(@"C:\Wurzel\sub\leaf");
        fs.AddFile(@"C:\Wurzel\.mdignore", "root-pattern\n", DateTime.UnixEpoch);
        fs.AddFile(@"C:\Wurzel\sub\.mdignore", "sub-pattern\n", DateTime.UnixEpoch);
        fs.AddFile(@"C:\Wurzel\sub\leaf\.mdignore", "leaf-pattern\n", DateTime.UnixEpoch);
        MdIgnoreHierarchy sut = NewHierarchy(fs);

        IReadOnlyList<string> result = sut.GetPatternsFor(@"C:\Wurzel", @"C:\Wurzel\sub\leaf");

        Assert.Equal(["root-pattern", "sub-pattern", "leaf-pattern"], result);
    }

    [Fact]
    public void GetPatternsFor_OnRepeatedCall_DoesNotReReadFromDisk()
    {
        CountingFileSystem fs = new();
        fs.AddDirectory(@"C:\Wurzel");
        fs.AddFile(@"C:\Wurzel\.mdignore", "**/temp/**\n");
        MdIgnoreHierarchy sut = NewHierarchy(fs);

        _ = sut.GetPatternsFor(@"C:\Wurzel", @"C:\Wurzel");
        _ = sut.GetPatternsFor(@"C:\Wurzel", @"C:\Wurzel");

        Assert.Equal(1, fs.ReadAllBytesCalls);
    }

    [Fact]
    public void Clear_RemovesCachedPatterns_NextCallRereads()
    {
        CountingFileSystem fs = new();
        fs.AddDirectory(@"C:\Wurzel");
        fs.AddFile(@"C:\Wurzel\.mdignore", "**/temp/**\n");
        MdIgnoreHierarchy sut = NewHierarchy(fs);

        _ = sut.GetPatternsFor(@"C:\Wurzel", @"C:\Wurzel");
        sut.Clear();
        _ = sut.GetPatternsFor(@"C:\Wurzel", @"C:\Wurzel");

        Assert.Equal(2, fs.ReadAllBytesCalls);
    }

    [Fact]
    public void GetPatternsFor_OnRelativePath_ResolvesToFullPath()
    {
        FakeFileSystem fs = new();
        string root = Path.GetFullPath(@"C:\Wurzel");
        fs.AddDirectory(root);
        fs.AddFile(Path.Combine(root, ".mdignore"), "root-pattern\n", DateTime.UnixEpoch);
        MdIgnoreHierarchy sut = NewHierarchy(fs);

        IReadOnlyList<string> result = sut.GetPatternsFor(root, root);

        Assert.Equal(["root-pattern"], result);
    }

    private static MdIgnoreHierarchy NewHierarchy(IFileSystem fileSystem) =>
        new(new MdIgnoreReader(fileSystem));

    /// <summary>Variante mit Read-Counter — eigenes Mini-Filesystem für Caching-Tests.</summary>
    private sealed class CountingFileSystem : IFileSystem
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

        public int ReadAllBytesCalls { get; private set; }

        public void AddDirectory(string path) => _directories.Add(path);

        public void AddFile(string path, string content) =>
            _files[path] = System.Text.Encoding.UTF8.GetBytes(content);

        public bool DirectoryExists(string path) => _directories.Contains(path);
        public bool FileExists(string path) => _files.ContainsKey(path);
        public void EnsureDirectoryExists(string path) => _ = _directories.Add(path);
        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) => [];
        public IEnumerable<string> EnumerateDirectories(string directory) => [];
        public bool IsReparsePoint(string path) => false;
        public string GetDirectoryFinalPath(string path) => path;
        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(ReadAllBytes(path));

        public byte[] ReadAllBytes(string path)
        {
            ReadAllBytesCalls++;
            return _files[path];
        }

        public Stream OpenRead(string path) => new MemoryStream(_files[path], writable: false);
        public DateTime GetLastWriteTimeUtc(string path) => DateTime.UnixEpoch;
        public long GetFileSize(string path) => _files[path].LongLength;
        public Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            _files[path] = content.ToArray();
            return Task.CompletedTask;
        }
    }
}
