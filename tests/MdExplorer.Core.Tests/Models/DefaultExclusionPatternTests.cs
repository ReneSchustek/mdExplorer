using MdExplorer.Core.Models;
using Microsoft.Extensions.FileSystemGlobbing;

namespace MdExplorer.Core.Tests.Models;

/// <summary>
/// Hält fest, was die Voreinstellung vom Index fernhält.
/// </summary>
/// <remarks>
/// Anlass ist die Auswertung eines gewachsenen Bestands am 16.08.2026: Von 33.485 indizierten
/// Dateien stammten 26.008 aus Fremdcode. Der größte einzelne Posten waren Kernkopien, der
/// zweitgrößte mit 3.906 Dateien die <c>vendor</c>-Ordner der PHP-Projekte — für die es kein
/// Muster gab, obwohl <c>node_modules</c> seit jeher eines hat.
/// </remarks>
public sealed class DefaultExclusionPatternTests
{
    [Theory]
    [InlineData("vendor/paket/README.md")]
    [InlineData("projekt/vendor/paket/doc/HANDBUCH.md")]
    [InlineData("node_modules/paket/README.md")]
    [InlineData(".git/hooks/README.md")]
    [InlineData("bin/Debug/notiz.md")]
    [InlineData("obj/Release/notiz.md")]
    [InlineData(".vs/notiz.md")]
    public void ForeignContentIsExcluded(string relativePath)
    {
        Assert.True(Matcher().Match(relativePath).HasMatches, relativePath + " sollte ausgeschlossen sein.");
    }

    /// <remarks>
    /// Die Gegenprobe: Ein Ausschluss, der zu viel greift, nimmt eigene Notizen mit. Ein
    /// Ordner, der bloß so heißt wie ein Ausschluss, ist keiner — <c>vendor-notizen</c> ist
    /// eine eigene Ablage, kein Paketverzeichnis.
    /// </remarks>
    [Theory]
    [InlineData("notizen/projekt.md")]
    [InlineData("vendor-notizen/projekt.md")]
    [InlineData("meine-bin-sammlung/projekt.md")]
    [InlineData("dokumentation/vendor.md")]
    public void OwnNotesStayIncluded(string relativePath)
    {
        Assert.False(Matcher().Match(relativePath).HasMatches, relativePath + " sollte im Index bleiben.");
    }

    [Fact]
    public void EveryPatternIsUsable()
    {
        foreach (string pattern in IndexingSettings.DefaultExclusionPatterns)
        {
            Assert.False(string.IsNullOrWhiteSpace(pattern));
            Assert.StartsWith("**/", pattern, StringComparison.Ordinal);
            Assert.EndsWith("/**", pattern, StringComparison.Ordinal);
        }
    }

    private static Matcher Matcher()
    {
        Matcher matcher = new(StringComparison.OrdinalIgnoreCase);
        foreach (string pattern in IndexingSettings.DefaultExclusionPatterns)
        {
            _ = matcher.AddInclude(pattern);
        }

        return matcher;
    }
}
