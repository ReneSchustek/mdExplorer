using System.IO;
using System.Text;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Settings;

namespace MdExplorer.Core.Tests.Settings;

public sealed class MdIgnoreReaderTests
{
    [Fact]
    public void Read_OnMissingFile_ReturnsEmpty()
    {
        InMemoryFs fs = new();
        MdIgnoreReader sut = new(fs);

        IReadOnlyList<string> result = sut.Read(@"C:\root");

        Assert.Empty(result);
    }

    [Fact]
    public void Read_OnFileWithCommentsAndBlankLines_FiltersThemOut()
    {
        InMemoryFs fs = new();
        fs.AddFile(@"C:\root\.mdignore",
            "# kommentar\n" +
            "\n" +
            "**/Drafts/**\n" +
            "  \n" +
            "!important.md\n");
        MdIgnoreReader sut = new(fs);

        IReadOnlyList<string> result = sut.Read(@"C:\root");

        Assert.Equal(["**/Drafts/**", "!important.md"], result);
    }

    [Fact]
    public void Read_OnUtf8BomFile_StripsBomBeforeParsing()
    {
        InMemoryFs fs = new();
        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] payload = Encoding.UTF8.GetBytes("**/Trash/**\n");
        byte[] bytes = new byte[bom.Length + payload.Length];
        Buffer.BlockCopy(bom, 0, bytes, 0, bom.Length);
        Buffer.BlockCopy(payload, 0, bytes, bom.Length, payload.Length);
        fs.AddRawFile(@"C:\root\.mdignore", bytes);
        MdIgnoreReader sut = new(fs);

        IReadOnlyList<string> result = sut.Read(@"C:\root");

        Assert.Equal(["**/Trash/**"], result);
    }

    private sealed class InMemoryFs : IFileSystem
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

        public void AddFile(string path, string content) => _files[path] = Encoding.UTF8.GetBytes(content);

        public void AddRawFile(string path, byte[] bytes) => _files[path] = bytes;

        public bool DirectoryExists(string path) => true;
        public bool FileExists(string path) => _files.ContainsKey(path);
        public void EnsureDirectoryExists(string path) { }
        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) => [];
        public IEnumerable<string> EnumerateDirectories(string directory) => [];
        public bool IsReparsePoint(string path) => false;
        public string GetDirectoryFinalPath(string path) => path;
        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) => Task.FromResult(_files[path]);
        public byte[] ReadAllBytes(string path) => _files[path];
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
