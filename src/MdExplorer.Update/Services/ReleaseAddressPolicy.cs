using System.Collections.Frozen;

namespace MdExplorer.Update.Services;

/// <summary>
/// Entscheidet, welchen Adressen aus der Release-Antwort die Anwendung folgt.
/// </summary>
/// <remarks>
/// Die Antwort der API ist Eingabe von außen. Ohne Prüfung öffnete die Oberfläche jede Adresse im
/// Browser, auch <c>file:</c>, und der Installer lüde von jedem Host, auch ohne TLS. Zugelassen
/// sind deshalb nur <c>https</c> und die Hosts, die GitHub für Release-Seiten und Anhänge
/// verwendet; Seite und Anhang müssen außerdem im konfigurierten Repository liegen.
/// </remarks>
internal static class ReleaseAddressPolicy
{
    private const string ReleasePageHost = "github.com";

    // github.com nimmt die Anfrage nach einem Anhang an und leitet auf einen der beiden
    // Speicher-Hosts weiter; die Weiterleitung trägt eine signierte Adresse ohne Repository-Pfad.
    private static readonly FrozenSet<string> TransferHosts = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        ReleasePageHost,
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com");

    /// <summary>Ob <paramref name="address"/> eine Release-Seite des Repositorys ist.</summary>
    public static bool IsReleasePage(Uri address, string owner, string repository) =>
        IsGitHubPath(address, owner, repository, "/releases/");

    /// <summary>Ob <paramref name="address"/> ein Anhang eines Releases dieses Repositorys ist.</summary>
    public static bool IsReleaseAsset(Uri address, string owner, string repository) =>
        IsGitHubPath(address, owner, repository, "/releases/download/");

    /// <summary>
    /// Ob von <paramref name="address"/> geladen werden darf. Geprüft wird auch die Adresse nach
    /// allen Weiterleitungen, deshalb ohne Repository-Pfad.
    /// </summary>
    public static bool IsTransferAddress(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return IsHttps(address) && TransferHosts.Contains(address.IdnHost);
    }

    private static bool IsGitHubPath(Uri address, string owner, string repository, string section)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        if (!IsHttps(address) || !string.Equals(address.IdnHost, ReleasePageHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // GitHub behandelt Eigentümer und Repository ohne Rücksicht auf Groß- und Kleinschreibung.
        // AbsolutePath ist bereits normalisiert, ein "/../" kann den Präfix also nicht umgehen.
        string prefix = string.Concat("/", owner, "/", repository, section);
        return address.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    // Ein abweichender Port wäre bei GitHub nie richtig und weist auf eine gebaute Adresse hin.
    private static bool IsHttps(Uri address) =>
        address.IsAbsoluteUri
        && string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
        && address.IsDefaultPort
        && string.IsNullOrEmpty(address.UserInfo);
}
