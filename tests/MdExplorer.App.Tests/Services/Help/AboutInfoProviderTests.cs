using System.IO;
using System.Xml.Linq;
using MdExplorer.App.Services.Help;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.App.Tests.Services.Help;

/// <summary>
/// Prüft den Über-Dialog-Lieferanten. Er liest Version und Build-Datum aus der laufenden
/// Anwendung; die Tests halten deshalb fest, was unabhängig vom Build-Stand gelten muss.
/// </summary>
public sealed class AboutInfoProviderTests
{
    // Weit in der Zukunft: Das Datum der laufenden Programmdatei liegt sicher davor.
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Read_InTheRunningAssembly_ReturnsANonEmptyVersion()
    {
        AboutInfoProvider sut = new(Clock);

        AboutInfo info = sut.Read();

        Assert.False(string.IsNullOrWhiteSpace(info.Version));
    }

    [Fact]
    public void Read_InTheRunningAssembly_ReturnsABuildDateNotInTheFuture()
    {
        AboutInfoProvider sut = new(Clock);

        AboutInfo info = sut.Read();

        // Das Datum stammt vom Zeitstempel der laufenden Datei oder ist die aktuelle Zeit;
        // in der Zukunft darf es in keinem Fall liegen.
        Assert.True(info.BuildDateUtc <= Clock.GetUtcNow().UtcDateTime, $"Build-Datum liegt in der Zukunft: {info.BuildDateUtc:O}");
        Assert.NotEqual(default, info.BuildDateUtc);
    }

    [Fact]
    public void Read_ForEveryLibrary_ListsNameAndLicense()
    {
        AboutInfoProvider sut = new(Clock);

        AboutInfo info = sut.Read();

        Assert.NotEmpty(info.Libraries);
        Assert.All(info.Libraries, lib =>
        {
            Assert.False(string.IsNullOrWhiteSpace(lib.Name));
            Assert.False(string.IsNullOrWhiteSpace(lib.License));
        });
    }

    [Fact]
    public void Read_ForEveryLibrary_ListsItOnlyOnce()
    {
        AboutInfoProvider sut = new(Clock);

        AboutInfo info = sut.Read();

        // Ein doppelter Eintrag im Dialog wirkt wie ein Pflegefehler und ist einer.
        Assert.Equal(info.Libraries.Count, info.Libraries.Select(lib => lib.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Read_ForEveryLicense_UsesAnSpdxIdentifier()
    {
        AboutInfoProvider sut = new(Clock);
        string[] allowed = ["MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause"];

        AboutInfo info = sut.Read();

        Assert.All(info.Libraries, lib => Assert.Contains(lib.License, allowed, StringComparer.Ordinal));
    }

    [Fact]
    public void Read_ForTheMainDependencies_MentionsThem()
    {
        AboutInfoProvider sut = new(Clock);

        AboutInfo info = sut.Read();
        string[] names = [.. info.Libraries.Select(lib => lib.Name)];

        // Stichprobe: Fehlen diese, ist die Liste nicht mehr gepflegt.
        Assert.Contains("Markdig", names, StringComparer.Ordinal);
        Assert.Contains("Microsoft.Data.Sqlite", names, StringComparer.Ordinal);
        Assert.Contains("Microsoft.Web.WebView2", names, StringComparer.Ordinal);
    }

    /// <remarks>
    /// Die MIT-Lizenz verlangt den Hinweis in jeder Weitergabe. Jedes Paket, das ein Projekt unter
    /// <c>src</c> einbindet und ausliefert, muss deshalb in der Liste stehen, als eigener Eintrag
    /// oder unter seiner Familie (<c>Serilog</c> deckt <c>Serilog.Sinks.File</c>). Pakete, die nur
    /// beim Bauen wirken (<c>PrivateAssets</c> = <c>all</c>), gehen nicht in die Auslieferung.
    /// </remarks>
    [Fact]
    public void Read_AgainstTheProjectFiles_CoversEveryShippedPackage()
    {
        string[] listed = [.. new AboutInfoProvider(Clock).Read().Libraries.Select(lib => lib.Name)];

        string[] missing =
        [
            .. ShippedPackages()
                .Where(package => !listed.Any(name => package == name || package.StartsWith(name + ".", StringComparison.Ordinal)))
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(missing.Length == 0, "Ohne Lizenzhinweis im Über-Dialog: " + string.Join(", ", missing));
    }

    [Fact]
    public void Read_CalledTwice_ReturnsEquivalentContent()
    {
        AboutInfoProvider sut = new(Clock);

        AboutInfo first = sut.Read();
        AboutInfo second = sut.Read();

        Assert.Equal(first.Version, second.Version, StringComparer.Ordinal);
        Assert.Equal(first.Libraries.Count, second.Libraries.Count);
    }

    private static IEnumerable<string> ShippedPackages() =>
        Directory.EnumerateFiles(RepositoryPaths.Source(), "*.csproj", SearchOption.AllDirectories)
            .SelectMany(project => XDocument.Load(project).Descendants("PackageReference"))
            .Where(reference => !string.Equals(
                (string?)reference.Attribute("PrivateAssets") ?? (string?)reference.Element("PrivateAssets"),
                "all",
                StringComparison.OrdinalIgnoreCase))
            .Select(reference => (string)reference.Attribute("Include")!)
            .Distinct(StringComparer.Ordinal);
}
