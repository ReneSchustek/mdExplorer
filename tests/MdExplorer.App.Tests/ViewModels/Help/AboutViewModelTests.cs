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
    /// <summary>Feste Zone für die Prüfung — zwei Stunden vor UTC, ohne Sommerzeitsprünge.</summary>
    private static readonly TimeZoneInfo TestZone =
        TimeZoneInfo.CreateCustomTimeZone("MdExplorer-TestZone", TimeSpan.FromHours(2), "TestZone", "TestZone");

    [Fact]
    public void Constructor_WithoutProvider_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new AboutViewModel(null!, new StubTimeProvider(TestZone)));

    [Fact]
    public void Constructor_WithoutTimeProvider_Throws()
    {
        StubAboutInfoProvider provider = new("1.0.0", new DateTime(2026, 5, 17, 8, 30, 0, DateTimeKind.Utc));

        _ = Assert.Throws<ArgumentNullException>(() => new AboutViewModel(provider, null!));
    }

    [Fact]
    public void Constructor_TakesVersionFromProviderUnchanged()
    {
        StubAboutInfoProvider provider = new("1.2.3+abcdef", new DateTime(2026, 5, 17, 8, 30, 0, DateTimeKind.Utc));

        AboutViewModel sut = new(provider, new StubTimeProvider(TestZone));

        Assert.Equal("1.2.3+abcdef", sut.Version, StringComparer.Ordinal);
    }

    [Fact]
    public void Constructor_ShowsBuildDateInTheLocalTimeZone()
    {
        StubAboutInfoProvider provider = new("1.0.0", new DateTime(2026, 5, 17, 8, 30, 0, DateTimeKind.Utc));

        AboutViewModel sut = new(provider, new StubTimeProvider(TestZone));

        // Aufgezeichnet wird in UTC, abgelesen an der Uhr des Rechners: 08:30 UTC sind hier 10:30.
        Assert.Equal("2026-05-17 10:30", sut.BuildDateDisplay, StringComparer.Ordinal);
        Assert.DoesNotContain("UTC", sut.BuildDateDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_KeepsLibraryListAsProvided()
    {
        LibraryInfo[] bibliotheken = [new("Alpha", "MIT"), new("Beta", "Apache-2.0")];
        StubAboutInfoProvider provider = new("1.0.0", DateTime.UtcNow, bibliotheken);

        AboutViewModel sut = new(provider, new StubTimeProvider(TestZone));

        Assert.Equal(2, sut.Libraries.Count);
        Assert.Equal("Alpha", sut.Libraries[0].Name, StringComparer.Ordinal);
        Assert.Equal("Apache-2.0", sut.Libraries[1].License, StringComparer.Ordinal);
    }

    [Fact]
    public void Constructor_ReadsFromProviderExactlyOnce()
    {
        StubAboutInfoProvider provider = new("1.0.0", DateTime.UtcNow);

        AboutViewModel sut = new(provider, new StubTimeProvider(TestZone));

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

        AboutViewModel sut = new(provider, new StubTimeProvider(TestZone));

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
            AboutViewModel sut = new(provider, new StubTimeProvider(TestZone));
            Assert.Equal("2026-12-24 20:05", sut.BuildDateDisplay, StringComparer.Ordinal);
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

    private sealed class StubTimeProvider(TimeZoneInfo zone) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => zone;
    }
}
