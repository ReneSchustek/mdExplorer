using MdExplorer.App.Services.Help;

namespace MdExplorer.App.Tests.Services.Help;

/// <summary>
/// Prüft den Über-Dialog-Lieferanten. Er liest Version und Build-Datum aus der laufenden
/// Anwendung; die Tests halten deshalb fest, was unabhängig vom Build-Stand gelten muss.
/// </summary>
public sealed class AboutInfoProviderTests
{
    [Fact]
    public void Read_ReturnsNonEmptyVersion()
    {
        AboutInfoProvider sut = new();

        AboutInfo info = sut.Read();

        Assert.False(string.IsNullOrWhiteSpace(info.Version));
    }

    [Fact]
    public void Read_ReturnsBuildDateThatIsNotInTheFuture()
    {
        AboutInfoProvider sut = new();

        AboutInfo info = sut.Read();

        // Das Datum stammt vom Zeitstempel der laufenden Datei oder ist die aktuelle Zeit;
        // in der Zukunft darf es in keinem Fall liegen.
        Assert.True(info.BuildDateUtc <= DateTime.UtcNow.AddMinutes(1), $"Build-Datum liegt in der Zukunft: {info.BuildDateUtc:O}");
        Assert.NotEqual(default, info.BuildDateUtc);
    }

    [Fact]
    public void Read_ListsLibrariesWithNameAndLicense()
    {
        AboutInfoProvider sut = new();

        AboutInfo info = sut.Read();

        Assert.NotEmpty(info.Libraries);
        Assert.All(info.Libraries, lib =>
        {
            Assert.False(string.IsNullOrWhiteSpace(lib.Name));
            Assert.False(string.IsNullOrWhiteSpace(lib.License));
        });
    }

    [Fact]
    public void Read_ListsEachLibraryOnlyOnce()
    {
        AboutInfoProvider sut = new();

        AboutInfo info = sut.Read();

        // Ein doppelter Eintrag im Dialog wirkt wie ein Pflegefehler und ist einer.
        Assert.Equal(info.Libraries.Count, info.Libraries.Select(lib => lib.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Read_UsesSpdxStyleLicenseIdentifiers()
    {
        AboutInfoProvider sut = new();
        string[] erlaubt = ["MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause"];

        AboutInfo info = sut.Read();

        Assert.All(info.Libraries, lib => Assert.Contains(lib.License, erlaubt, StringComparer.Ordinal));
    }

    [Fact]
    public void Read_MentionsTheLibrariesTheApplicationActuallyDependsOn()
    {
        AboutInfoProvider sut = new();

        AboutInfo info = sut.Read();
        string[] namen = [.. info.Libraries.Select(lib => lib.Name)];

        // Stichprobe: Fehlen diese, ist die Liste nicht mehr gepflegt.
        Assert.Contains("Markdig", namen, StringComparer.Ordinal);
        Assert.Contains("Microsoft.Data.Sqlite", namen, StringComparer.Ordinal);
        Assert.Contains("Microsoft.Web.WebView2", namen, StringComparer.Ordinal);
    }

    [Fact]
    public void Read_ReturnsEquivalentContentOnRepeatedCalls()
    {
        AboutInfoProvider sut = new();

        AboutInfo erst = sut.Read();
        AboutInfo zweit = sut.Read();

        Assert.Equal(erst.Version, zweit.Version, StringComparer.Ordinal);
        Assert.Equal(erst.Libraries.Count, zweit.Libraries.Count);
    }
}
