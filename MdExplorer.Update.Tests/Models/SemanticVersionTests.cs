using MdExplorer.Update.Models;

namespace MdExplorer.Update.Tests.Models;

/// <summary>Tabellentests für das Parsen und die Präzedenz von <see cref="SemanticVersion"/>.</summary>
public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("v0.9.0", 0, 9, 0, null)]
    [InlineData("V2.0.0", 2, 0, 0, null)]
    [InlineData("1.2", 1, 2, 0, null)]
    [InlineData("3", 3, 0, 0, null)]
    [InlineData("  1.4.0  ", 1, 4, 0, null)]
    [InlineData("1.2.3-beta.1", 1, 2, 3, "beta.1")]
    [InlineData("1.2.3-rc1+build.7", 1, 2, 3, "rc1")]
    [InlineData("1.2.3+sha.abc", 1, 2, 3, null)]
    public void TryParse_AcceptsValidVersions(string input, int major, int minor, int patch, string? preRelease)
    {
        bool parsed = SemanticVersion.TryParse(input, out SemanticVersion version);

        Assert.True(parsed);
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(preRelease, version.PreRelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.2.3.4")]
    [InlineData("1..2")]
    [InlineData("1.-2.3")]
    [InlineData("1.2.3-")]
    [InlineData("v")]
    public void TryParse_RejectsInvalidVersions(string? input)
    {
        bool parsed = SemanticVersion.TryParse(input, out SemanticVersion version);

        Assert.False(parsed);
        Assert.Equal(default, version);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.0", "1.1.0")]
    [InlineData("1.9.9", "2.0.0")]
    [InlineData("1.0.0-beta", "1.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0-beta")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-1", "1.0.0-2")]
    [InlineData("1.0.0-1", "1.0.0-alpha")]
    public void CompareTo_OrdersLowerBeforeHigher(string lower, string higher)
    {
        Assert.True(SemanticVersion.TryParse(lower, out SemanticVersion low));
        Assert.True(SemanticVersion.TryParse(higher, out SemanticVersion high));

        Assert.True(low < high);
        Assert.True(high > low);
        Assert.True(low != high);
    }

    [Fact]
    public void Equality_TreatsIdenticalVersionsAsEqual()
    {
        Assert.True(SemanticVersion.TryParse("1.2.3-rc.1", out SemanticVersion left));
        Assert.True(SemanticVersion.TryParse("v1.2.3-rc.1", out SemanticVersion right));

        Assert.True(left == right);
        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.Equal(0, left.CompareTo(right));
    }

    [Fact]
    public void ToString_RoundTripsThroughTryParse()
    {
        SemanticVersion original = new(1, 4, 2, "beta.3");

        string text = original.ToString();

        Assert.Equal("1.4.2-beta.3", text);
        Assert.True(SemanticVersion.TryParse(text, out SemanticVersion roundTripped));
        Assert.Equal(original, roundTripped);
    }
}
