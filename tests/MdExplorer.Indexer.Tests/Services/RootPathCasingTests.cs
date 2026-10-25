using MdExplorer.Indexer.Services;
using MdExplorer.Indexer.Tests.Fakes;

namespace MdExplorer.Indexer.Tests.Services;

/// <summary>
/// Der Indexer arbeitet mit der Wurzel in der Schreibweise der Platte, nicht in der aus den
/// Einstellungen.
/// </summary>
/// <remarks>
/// Windows liefert beim Aufzählen den Wurzelpfad so zurück, wie er übergeben wurde. Steht in den
/// Einstellungen <c>C:\übersicht</c> und auf der Platte <c>C:\Übersicht</c>, entstehen Pfade, die
/// die Datenbank nicht als gleich erkennt: Ihre NOCASE-Collation faltet nur ASCII-Buchstaben. Die
/// Datei stünde dann zweimal im Index.
/// </remarks>
public sealed class RootPathCasingTests
{
    [Fact]
    public void Canonicalize_TakesTheFolderNamesFromTheDisk()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(@"C:\Übersicht");
        fs.AddDirectory(@"C:\Übersicht\Ärger");

        string canonical = RootPathCasing.Canonicalize(fs, @"C:\übersicht\ärger");

        Assert.Equal(@"C:\Übersicht\Ärger", canonical, StringComparer.Ordinal);
    }

    [Fact]
    public void Canonicalize_AFolderTheDiskDoesNotKnow_KeepsTheGivenName()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(@"C:\Übersicht");

        string canonical = RootPathCasing.Canonicalize(fs, @"C:\übersicht\neu");

        Assert.Equal(@"C:\Übersicht\neu", canonical, StringComparer.Ordinal);
    }

    [Fact]
    public void Canonicalize_ADriveRoot_StaysAsItIs()
    {
        Assert.Equal(@"C:\", RootPathCasing.Canonicalize(new FakeFileSystem(), @"C:\"), StringComparer.Ordinal);
    }
}
