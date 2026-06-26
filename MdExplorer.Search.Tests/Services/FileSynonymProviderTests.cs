using System.Text;
using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Options;
using MdExplorer.Search.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// Tests für den dateibasierten Synonym-Provider. Fehlende Datei,
/// fehlerhafte JSON und valide Map werden verifiziert.
/// </summary>
public sealed class FileSynonymProviderTests
{
    [Fact]
    public void GetSynonyms_WhenFilePathIsNull_ReturnsEmpty()
    {
        StubFileSystem fs = new();
        FileSynonymProvider sut = NewProvider(fs, synonymFilePath: null);

        Assert.Empty(sut.GetSynonyms("auto"));
    }

    [Fact]
    public void GetSynonyms_WhenFileDoesNotExist_ReturnsEmpty()
    {
        StubFileSystem fs = new();
        FileSynonymProvider sut = NewProvider(fs, synonymFilePath: @"C:\nicht\da\synonyms.json");

        Assert.Empty(sut.GetSynonyms("auto"));
    }

    [Fact]
    public void GetSynonyms_LoadsAndCachesValidJson()
    {
        StubFileSystem fs = new();
        fs.WriteText(@"C:\syn.json", """{"auto":["wagen","fahrzeug"]}""");
        FileSynonymProvider sut = NewProvider(fs, @"C:\syn.json");

        IReadOnlyList<string> first = sut.GetSynonyms("auto");
        IReadOnlyList<string> second = sut.GetSynonyms("auto");

        Assert.Equal(["wagen", "fahrzeug"], first);
        _ = Assert.Single(fs.ReadCalls);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GetSynonyms_OnInvalidJson_ReturnsEmptyAndLogs()
    {
        StubFileSystem fs = new();
        fs.WriteText(@"C:\syn.json", "{ kaputt :: ");
        FileSynonymProvider sut = NewProvider(fs, @"C:\syn.json");

        Assert.Empty(sut.GetSynonyms("auto"));
    }

    [Fact]
    public void GetSynonyms_LookupIsCaseInsensitive()
    {
        StubFileSystem fs = new();
        fs.WriteText(@"C:\syn.json", """{"Auto":["wagen"]}""");
        FileSynonymProvider sut = NewProvider(fs, @"C:\syn.json");

        Assert.Equal(["wagen"], sut.GetSynonyms("AUTO"));
        Assert.Equal(["wagen"], sut.GetSynonyms("auto"));
    }

    private static FileSynonymProvider NewProvider(IFileSystem fileSystem, string? synonymFilePath)
    {
        SearchOptions options = new() { SynonymFilePath = synonymFilePath };
        return new FileSynonymProvider(
            fileSystem,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<FileSynonymProvider>.Instance);
    }

    private sealed class StubFileSystem : IFileSystem
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

        public List<string> ReadCalls { get; } = [];

        public void WriteText(string path, string content) =>
            _files[path] = Encoding.UTF8.GetBytes(content);

        public bool DirectoryExists(string path) => true;
        public bool FileExists(string path) => _files.ContainsKey(path);
        public void EnsureDirectoryExists(string path) { }
        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) => [];
        public IEnumerable<string> EnumerateDirectories(string directory) => [];
        public bool IsReparsePoint(string path) => false;
        public string GetDirectoryFinalPath(string path) => path;

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(_files[path]);

        public byte[] ReadAllBytes(string path)
        {
            ReadCalls.Add(path);
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
