using System.Globalization;
using MdExplorer.App.Services.Help;
using MdExplorer.App.ViewModels.Help;

namespace MdExplorer.App.Tests.ViewModels.Help;

/// <summary>
/// Prüft das ViewModel des Über-Dialogs. Es liest einmalig beim Erzeugen; die Tests halten
/// fest, dass die gelieferten Werte unverändert übernommen und lesbar formatiert werden.
/// </summary>
public sealed class AboutViewModelTests
{
    [Fact]
    public void Constructor_WithoutProvider_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new AboutViewModel(null!));

    [Fact]
    public void Constructor_TakesVersionFromProviderUnchanged()
    {
        StubAboutInfoProvider provider = new("1.2.3+abcdef", new DateTime(2026, 5, 17, 8, 30, 0, DateTimeKind.Utc));

        AboutViewModel sut = new(provider);

        Assert.Equal("1.2.3+abcdef", sut.Version, StringComparer.Ordinal);
    }

    [Fact]
    public void Constructor_FormatsBuildDateAsInvariantUtc()
    {
        StubAboutInfoProvider provider = new("1.0.0", new DateTime(2026, 5, 17, 8, 30, 0, DateTimeKind.Utc));

        AboutViewModel sut = new(provider);

        // Fest formatiert und kulturunabhängig: Der Dialog soll überall dasselbe zeigen.
        Assert.Equal("2026-05-17 08:30 UTC", sut.BuildDateDisplay, StringComparer.Ordinal);
    }

    [Fact]
    public void Constructor_KeepsLibraryListAsProvided()
    {
        LibraryInfo[] bibliotheken = [new("Alpha", "MIT"), new("Beta", "Apache-2.0")];
        StubAboutInfoProvider provider = new("1.0.0", DateTime.UtcNow, bibliotheken);

        AboutViewModel sut = new(provider);

        Assert.Equal(2, sut.Libraries.Count);
        Assert.Equal("Alpha", sut.Libraries[0].Name, StringComparer.Ordinal);
        Assert.Equal("Apache-2.0", sut.Libraries[1].License, StringComparer.Ordinal);
    }

    [Fact]
    public void Constructor_ReadsFromProviderExactlyOnce()
    {
        StubAboutInfoProvider provider = new("1.0.0", DateTime.UtcNow);

        AboutViewModel sut = new(provider);

        Assert.Equal(1, provider.ReadCount);
        // Wiederholtes Lesen der Eigenschaften darf den Lieferanten nicht erneut befragen.
        _ = sut.Version;
        _ = sut.BuildDateDisplay;
        Assert.Equal(1, provider.ReadCount);
    }

    [Fact]
    public void DonationVisibility_MatchesTheConfiguredState()
    {
        StubAboutInfoProvider provider = new("1.0.0", DateTime.UtcNow);

        AboutViewModel sut = new(provider);

        // Der Eintrag darf nur sichtbar sein, wenn eine echte Adresse hinterlegt ist —
        // ein toter Link im Dialog wäre schlimmer als gar keiner.
        Assert.Equal(SupportDonation.IsConfigured, sut.IsDonationVisible);
        Assert.Equal(SupportDonation.PayPalUrl, sut.DonationUrl, StringComparer.Ordinal);
        if (sut.IsDonationVisible)
        {
            Assert.StartsWith("https://", sut.DonationUrl, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BuildDateDisplay_IsIndependentOfTheCurrentCulture()
    {
        StubAboutInfoProvider provider = new("1.0.0", new DateTime(2026, 12, 24, 18, 5, 0, DateTimeKind.Utc));
        CultureInfo vorher = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            AboutViewModel sut = new(provider);
            Assert.Equal("2026-12-24 18:05 UTC", sut.BuildDateDisplay, StringComparer.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = vorher;
        }
    }

    private sealed class StubAboutInfoProvider(string version, DateTime buildDateUtc, IReadOnlyList<LibraryInfo>? libraries = null)
        : IAboutInfoProvider
    {
        public int ReadCount { get; private set; }

        public AboutInfo Read()
        {
            ReadCount++;
            return new AboutInfo(version, buildDateUtc, libraries ?? [new LibraryInfo("Beispiel", "MIT")]);
        }
    }
}
