namespace MdExplorer.Update.Models;

/// <summary>
/// Das herunterladbare Installationspaket eines Releases samt erwartetem Prüfwert.
/// </summary>
/// <param name="FileName">Dateiname des Pakets, wie im Release veröffentlicht.</param>
/// <param name="DownloadUrl">Direkte Download-Adresse des Pakets.</param>
/// <param name="ExpectedSha256">
/// Erwarteter SHA-256-Hash in Hex-Schreibweise, oder <see langword="null"/>, wenn das Release
/// keine Prüfsummen-Datei mitliefert.
/// <para>
/// Ohne Prüfwert wird <b>nicht</b> installiert. Der Installer ist nicht signiert; die Prüfsumme
/// ist damit der einzige Beleg dafür, dass die heruntergeladene Datei die veröffentlichte ist.
/// Ein Update ohne diesen Beleg wäre ein ungeprüfter Programmstart aus dem Netz — genau das,
/// wogegen die Prüfung schützen soll.
/// </para>
/// </param>
public sealed record UpdateAsset(string FileName, Uri DownloadUrl, string? ExpectedSha256)
{
    /// <summary><see langword="true"/>, wenn ein Prüfwert vorliegt und damit installiert werden darf.</summary>
    public bool IsVerifiable => !string.IsNullOrWhiteSpace(ExpectedSha256);
}
