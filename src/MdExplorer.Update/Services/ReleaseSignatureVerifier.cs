using System.Security.Cryptography;

namespace MdExplorer.Update.Services;

/// <summary>
/// Prüft die Signatur eines Installationspakets gegen den öffentlichen Schlüssel, der in die
/// Anwendung eingebaut ist.
/// </summary>
/// <remarks>
/// <para>
/// Die Prüfsumme kommt aus demselben Release wie das Paket; wer das Release übernimmt, liefert
/// beide passend. Die Signatur entsteht dagegen mit einem privaten Schlüssel, der nie auf GitHub
/// liegt, und belegt damit die Herkunft.
/// </para>
/// <para>
/// Verfahren: ECDSA mit P-256 über SHA-256 der Datei, Signatur im DER-Format. Das ist dasselbe,
/// was der Release-Workflow erzeugt und was <c>openssl dgst -sha256 -sign</c> liefert.
/// Fehlt der Schlüssel im Build, gilt nichts als geprüft, und die Anwendung installiert nicht
/// selbst; der Weg über die Release-Seite bleibt.
/// </para>
/// </remarks>
internal sealed class ReleaseSignatureVerifier
{
    /// <summary>Name der eingebetteten Ressource mit dem öffentlichen Schlüssel (PEM).</summary>
    internal const string PublicKeyResourceName = "MdExplorer.Update.release-public-key.pem";

    private readonly string? _publicKeyPem;

    /// <summary>Erzeugt den Prüfer für einen öffentlichen Schlüssel im PEM-Format.</summary>
    /// <param name="publicKeyPem">SubjectPublicKeyInfo als PEM, oder <see langword="null"/>, wenn keiner vorliegt.</param>
    internal ReleaseSignatureVerifier(string? publicKeyPem)
    {
        _publicKeyPem = string.IsNullOrWhiteSpace(publicKeyPem) ? null : publicKeyPem;
    }

    /// <summary>Ob ein Schlüssel vorliegt und damit überhaupt geprüft werden kann.</summary>
    public bool IsConfigured => _publicKeyPem is not null;

    /// <summary>Liest den Schlüssel aus der eingebetteten Ressource dieser Assembly.</summary>
    public static ReleaseSignatureVerifier FromEmbeddedKey()
    {
        using Stream? resource = typeof(ReleaseSignatureVerifier).Assembly.GetManifestResourceStream(PublicKeyResourceName);
        if (resource is null)
        {
            return new ReleaseSignatureVerifier(null);
        }

        using StreamReader reader = new(resource);
        return new ReleaseSignatureVerifier(reader.ReadToEnd());
    }

    /// <summary>Prüft <paramref name="signature"/> über den Inhalt von <paramref name="content"/>.</summary>
    /// <returns><see langword="false"/> auch dann, wenn kein Schlüssel vorliegt oder die Signatur kaputt ist.</returns>
    public bool Verify(Stream content, ReadOnlyMemory<byte> signature)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (_publicKeyPem is null || signature.IsEmpty)
        {
            return false;
        }

        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportFromPem(_publicKeyPem);
            return key.VerifyData(content, signature.ToArray(), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            // Ein unlesbarer Schlüssel oder eine Signatur, die kein DER ist, beweist nichts.
            return false;
        }
    }
}
