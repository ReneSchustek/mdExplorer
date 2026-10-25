using MdExplorer.Update.Services;

namespace MdExplorer.Update.Tests.Services;

/// <summary>
/// Hält fest, welchen Adressen aus der Release-Antwort gefolgt wird: nur https, nur GitHub und
/// seine Speicher-Hosts, Seiten und Anhänge nur im eigenen Repository.
/// </summary>
public sealed class ReleaseAddressPolicyTests
{
    private const string Owner = "ReneSchustek";
    private const string Repository = "mdExplorer";

    [Theory]
    [InlineData("https://github.com/ReneSchustek/mdExplorer/releases/tag/v1.0.0", true)]
    [InlineData("https://GITHUB.com/reneschustek/MDEXPLORER/releases/tag/v1.0.0", true)]
    [InlineData("http://github.com/ReneSchustek/mdExplorer/releases/tag/v1.0.0", false)]
    [InlineData("https://github.com:8443/ReneSchustek/mdExplorer/releases/tag/v1.0.0", false)]
    [InlineData("https://user@github.com/ReneSchustek/mdExplorer/releases/tag/v1.0.0", false)]
    [InlineData("https://github.com/ReneSchustek/mdExplorerX/releases/tag/v1.0.0", false)]
    [InlineData("https://github.com/ReneSchustek/mdExplorer/issues/1", false)]
    [InlineData("https://github.com.example.invalid/ReneSchustek/mdExplorer/releases/tag/v1.0.0", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    public void IsReleasePage_DecidesByScheme_HostAndRepository(string address, bool expected)
    {
        Assert.Equal(expected, ReleaseAddressPolicy.IsReleasePage(new Uri(address), Owner, Repository));
    }

    [Theory]
    [InlineData("https://github.com/ReneSchustek/mdExplorer/releases/download/v1.0.0/MdExplorer-1.0.0-setup.exe", true)]
    [InlineData("https://github.com/ReneSchustek/mdExplorer/releases/tag/v1.0.0", false)]
    [InlineData("https://github.com/ReneSchustek/mdExplorer/releases/download/../../../../Fremder/x/releases/download/a.exe", false)]
    [InlineData("https://objects.githubusercontent.com/ReneSchustek/mdExplorer/releases/download/v1/a.exe", false)]
    public void IsReleaseAsset_AcceptsOnlyDownloadsOfTheOwnRepository(string address, bool expected)
    {
        Assert.Equal(expected, ReleaseAddressPolicy.IsReleaseAsset(new Uri(address), Owner, Repository));
    }

    [Theory]
    [InlineData("https://github.com/any/path", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1", true)]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1", true)]
    [InlineData("http://objects.githubusercontent.com/github-production-release-asset/1", false)]
    [InlineData("https://raw.githubusercontent.com/x/y/main/setup.exe", false)]
    [InlineData("https://example.invalid/setup.exe", false)]
    public void IsTransferAddress_AcceptsOnlyGitHubAndItsReleaseStorage(string address, bool expected)
    {
        Assert.Equal(expected, ReleaseAddressPolicy.IsTransferAddress(new Uri(address)));
    }
}
