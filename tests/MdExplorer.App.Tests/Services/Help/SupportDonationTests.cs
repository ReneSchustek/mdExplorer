using MdExplorer.App.Services.Help;

namespace MdExplorer.App.Tests.Services.Help;

/// <summary>
/// Sichert die Manipulationssicherheit des Spendenziels ab: die Ziel-URL ist eine
/// Compile-Zeit-Konstante auf der offiziellen PayPal-Domain, per HTTPS, ohne
/// Laufzeit-Parameter. Es gibt keinen Settings-/DB-/Netz-Pfad, der sie verändern könnte.
/// </summary>
public sealed class SupportDonationTests
{
    [Fact]
    public void PayPalUrl_IsAbsoluteHttpsUri()
    {
        bool parsed = Uri.TryCreate(SupportDonation.PayPalUrl, UriKind.Absolute, out Uri? uri);

        Assert.True(parsed);
        Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
    }

    [Fact]
    public void PayPalUrl_TargetsOfficialPayPalDomain()
    {
        Uri uri = new(SupportDonation.PayPalUrl);

        bool isOfficial = uri.Host.Equals("paypal.me", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("paypal.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("www.paypal.com", StringComparison.OrdinalIgnoreCase);

        Assert.True(isOfficial, $"Unerwartete Host-Domain: {uri.Host}");
    }

    [Fact]
    public void PayPalUrl_CarriesNoRuntimeQueryParameters()
    {
        Uri uri = new(SupportDonation.PayPalUrl);

        Assert.True(string.IsNullOrEmpty(uri.Query));
    }

    [Fact]
    public void IsConfigured_IsTrue_WhenRealHandleIsSet()
    {
        // Nur mit einem echten Handle statt des Platzhalters erscheint der Spenden-Eintrag
        // im Über-Dialog. Fällt der Test um, fehlt der Eintrag in der ausgelieferten Fassung.
        Assert.True(SupportDonation.IsConfigured);
    }
}
