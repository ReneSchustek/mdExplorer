using System.Text;
using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Options;
using MdExplorer.Search.Services;
using MdExplorer.Search.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// Der Synonym-Anbieter liest die Synonymdatei einmal, sucht ohne Rücksicht auf Groß- und
/// Kleinschreibung und liefert bei fehlender oder kaputter Datei eine leere Liste. Eine kaputte
/// Datei darf die Suche nicht abbrechen.
/// </summary>
public sealed class FileSynonymProviderTests
{
    [Fact]
    public void GetSynonyms_WhenFilePathIsNull_ReturnsEmpty()
    {
        FakeFileSystem fs = new();
        FileSynonymProvider sut = NewProvider(fs, synonymFilePath: null);

        Assert.Empty(sut.GetSynonyms("auto"));
    }

    [Fact]
    public void GetSynonyms_WhenFileDoesNotExist_ReturnsEmpty()
    {
        FakeFileSystem fs = new();
        FileSynonymProvider sut = NewProvider(fs, synonymFilePath: @"C:\nicht\da\synonyms.json");

        Assert.Empty(sut.GetSynonyms("auto"));
    }

    [Fact]
    public void GetSynonyms_LoadsAndCachesValidJson()
    {
        FakeFileSystem fs = new();
        fs.AddFile(@"C:\syn.json", """{"auto":["wagen","fahrzeug"]}""");
        FileSynonymProvider sut = NewProvider(fs, @"C:\syn.json");

        IReadOnlyList<string> first = sut.GetSynonyms("auto");
        IReadOnlyList<string> second = sut.GetSynonyms("auto");

        Assert.Equal(["wagen", "fahrzeug"], first);
        _ = Assert.Single(fs.ReadCalls);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GetSynonyms_ForAnInflectedEntry_AnswersTheStemOfTheQuery()
    {
        // Die Anfrage schlägt mit dem Stamm nach („Wagen“ → „Wag“). Ein Eintrag in Grundform
        // träfe sonst nie, sobald der Stemmer eine Endung abtrennt.
        FakeFileSystem fs = new();
        fs.AddFile(@"C:\syn.json", """{"wagen":["auto"]}""");
        FileSynonymProvider sut = NewProvider(fs, @"C:\syn.json");

        Assert.Equal(["auto"], sut.GetSynonyms(GermanStemmer.Stem("Wagen")));
    }

    [Fact]
    public void GetSynonyms_ForTwoEntriesWithTheSameStem_MergesTheirSynonyms()
    {
        FakeFileSystem fs = new();
        fs.AddFile(@"C:\syn.json", """{"wagen":["auto"],"wages":["fahrzeug","auto"]}""");
        FileSynonymProvider sut = NewProvider(fs, @"C:\syn.json");

        Assert.Equal(["auto", "fahrzeug"], sut.GetSynonyms("wag"));
    }

    [Fact]
    public void GetSynonyms_OnInvalidJson_ReturnsEmpty()
    {
        FakeFileSystem fs = new();
        fs.AddFile(@"C:\syn.json", "{ kaputt :: ");
        FileSynonymProvider sut = NewProvider(fs, @"C:\syn.json");

        Assert.Empty(sut.GetSynonyms("auto"));
    }

    [Fact]
    public void GetSynonyms_LookupIsCaseInsensitive()
    {
        FakeFileSystem fs = new();
        fs.AddFile(@"C:\syn.json", """{"Auto":["wagen"]}""");
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
}
