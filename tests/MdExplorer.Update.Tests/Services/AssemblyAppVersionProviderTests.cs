using MdExplorer.Update.Models;
using MdExplorer.Update.Services;

namespace MdExplorer.Update.Tests.Services;

/// <summary>
/// Die installierte Version entscheidet, ob ein Update angeboten wird. Landet sie fälschlich bei
/// <c>0.0.0</c>, gilt jede veröffentlichte Fassung als neuer, und die Anwendung bietet bei jedem
/// Start ein Update an, das keines ist.
/// </summary>
public sealed class AssemblyAppVersionProviderTests
{
    [Fact]
    public void Resolve_PrefersTheInformationalVersion()
    {
        SemanticVersion version = AssemblyAppVersionProvider.Resolve("1.4.2+abc123", new Version(9, 9, 9, 0));

        Assert.Equal(new SemanticVersion(1, 4, 2), version);
    }

    [Fact]
    public void Resolve_WithoutInformationalVersion_FallsBackToTheFourPartAssemblyVersion()
    {
        // Die Assembly-Version hat immer vier Stellen. Die vierte trägt keine Bedeutung für den
        // Vergleich mit einer veröffentlichten Fassung.
        SemanticVersion version = AssemblyAppVersionProvider.Resolve(null, new Version(1, 2, 3, 0));

        Assert.Equal(new SemanticVersion(1, 2, 3), version);
    }

    [Fact]
    public void Resolve_WithATwoPartAssemblyVersion_TakesZeroAsPatch()
    {
        SemanticVersion version = AssemblyAppVersionProvider.Resolve("kaputt", new Version(2, 5));

        Assert.Equal(new SemanticVersion(2, 5, 0), version);
    }

    [Fact]
    public void Resolve_WithoutAnyVersion_ReturnsZero()
    {
        Assert.Equal(new SemanticVersion(0, 0, 0), AssemblyAppVersionProvider.Resolve(null, null));
    }

    [Fact]
    public void Constructor_ReadsTheVersionOfTheGivenAssembly()
    {
        // Die Testassembly trägt dieselbe Fassung aus VERSION wie die Anwendung; die
        // vierstellige Assembly-Version ist die unabhängige Gegenrechnung.
        System.Reflection.Assembly assembly = typeof(AssemblyAppVersionProviderTests).Assembly;
        Version expected = assembly.GetName().Version ?? throw new InvalidOperationException("Testassembly ohne Version.");

        AssemblyAppVersionProvider sut = new(assembly);

        Assert.Equal(new SemanticVersion(expected.Major, expected.Minor, expected.Build), sut.CurrentVersion);
        Assert.NotEqual(new SemanticVersion(0, 0, 0), sut.CurrentVersion);
    }
}
