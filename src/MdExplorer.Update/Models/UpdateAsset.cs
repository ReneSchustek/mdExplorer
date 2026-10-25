namespace MdExplorer.Update.Models;

/// <summary>
/// Das herunterladbare Installationspaket eines Releases samt erwartetem Prüfwert und Signatur.
/// </summary>
/// <param name="FileName">Dateiname des Pakets, wie im Release veröffentlicht.</param>
/// <param name="DownloadUrl">Direkte Download-Adresse des Pakets.</param>
/// <param name="ExpectedSha256">
/// Erwarteter SHA-256-Hash in Hex-Schreibweise, oder <see langword="null"/>, wenn das Release
/// keine Prüfsummen-Datei mitliefert. Er erkennt einen abgebrochenen oder unterwegs veränderten
/// Download, belegt aber nicht die Herkunft: Er stammt aus demselben Release wie das Paket.
/// </param>
/// <param name="Signature">
/// ECDSA-Signatur des Pakets im DER-Format, leer, wenn das Release keine mitliefert. Erst sie
/// belegt, dass das Paket vom Herausgeber stammt; ohne sie wird nicht installiert.
/// </param>
public sealed record UpdateAsset(string FileName, Uri DownloadUrl, string? ExpectedSha256, ReadOnlyMemory<byte> Signature)
{
    /// <summary>Paket ohne Signatur; installiert wird es nicht.</summary>
    public UpdateAsset(string fileName, Uri downloadUrl, string? expectedSha256)
        : this(fileName, downloadUrl, expectedSha256, ReadOnlyMemory<byte>.Empty)
    {
    }

    /// <summary><see langword="true"/>, wenn Prüfwert und Signatur vorliegen und damit installiert werden darf.</summary>
    public bool IsVerifiable => !string.IsNullOrWhiteSpace(ExpectedSha256) && !Signature.IsEmpty;
}
