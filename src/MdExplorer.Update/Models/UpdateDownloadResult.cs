namespace MdExplorer.Update.Models;

/// <summary>Ausgang eines Download- und Prüfvorgangs.</summary>
public enum UpdateDownloadStatus
{
    /// <summary>Paket geladen und Prüfwert stimmt überein.</summary>
    Verified = 0,

    /// <summary>Download fehlgeschlagen (kein Netz, Timeout, HTTP-Fehler).</summary>
    DownloadFailed = 1,

    /// <summary>
    /// Der berechnete Hash weicht vom veröffentlichten ab. Die Datei wird verworfen:
    /// eine Abweichung bedeutet einen abgebrochenen oder unterwegs veränderten Download,
    /// und beides darf man nicht ausführen.
    /// </summary>
    ChecksumMismatch = 2,

    /// <summary>Das Paket trägt keinen veröffentlichten Prüfwert; es wird nicht installiert.</summary>
    NoChecksumPublished = 3,

    /// <summary>Das Paket ließ sich lokal nicht ablegen.</summary>
    StorageFailed = 4,

    /// <summary>
    /// Das Release liefert keine Signatur mit, oder diese Fassung kennt keinen Schlüssel, gegen
    /// den sie prüfen könnte. Es wird nicht installiert.
    /// </summary>
    NoSignaturePublished = 5,

    /// <summary>
    /// Die Signatur passt nicht zum Paket. Prüfsumme und Paket stimmen dann zwar überein, das
    /// Paket stammt aber nicht vom Herausgeber; die Datei wird verworfen.
    /// </summary>
    SignatureInvalid = 6,
}

/// <summary>
/// Ergebnis von <see cref="Abstractions.IUpdateInstaller.DownloadAndVerifyAsync"/>.
/// </summary>
/// <param name="Status">Ausgang des Vorgangs.</param>
/// <param name="InstallerPath">Lokaler Pfad des geprüften Pakets, sonst <see langword="null"/>.</param>
public sealed record UpdateDownloadResult(UpdateDownloadStatus Status, string? InstallerPath)
{
    /// <summary><see langword="true"/>, wenn das Paket geprüft bereitliegt.</summary>
    public bool IsVerified => Status == UpdateDownloadStatus.Verified && InstallerPath is not null;

    /// <summary>Erfolgsergebnis mit lokalem Pfad.</summary>
    public static UpdateDownloadResult Verified(string installerPath) =>
        new(UpdateDownloadStatus.Verified, installerPath);

    /// <summary>Fehlerergebnis ohne Pfad.</summary>
    public static UpdateDownloadResult Failed(UpdateDownloadStatus status) => new(status, null);
}
