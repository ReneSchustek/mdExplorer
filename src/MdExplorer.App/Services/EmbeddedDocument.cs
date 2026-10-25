using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MdExplorer.App.Services;

/// <summary>
/// Gemeinsame Teile der Dokumente, die Hilfe- und Graph-Fenster aus eingebetteten Vorlagen
/// zusammensetzen: Vorlage lesen, Nonce erzeugen, Platzhalter füllen.
/// </summary>
/// <remarks>
/// Die Reihenfolge des Füllens ist sicherheitsrelevant und steht deshalb nur hier. Die Nutzlast
/// aus dem Bestand kommt zuletzt: Stünde in einem Titel ein Platzhalter wie
/// <c>__INLINE_JS__</c>, ersetzte ein späteres Replace ihn sonst mitten in den Daten.
/// </remarks>
internal static class EmbeddedDocument
{
    /// <summary>Platzhalter für die Nonce, die Skripte und Stile dieses Dokuments freigibt.</summary>
    public const string NoncePlaceholder = "__NONCE__";

    // 128 Bit Zufall, die Mindestlänge, die die CSP-Spezifikation für eine Nonce empfiehlt.
    private const int NonceByteLength = 16;

    /// <summary>Liest eine eingebettete Datei der Anwendung.</summary>
    /// <param name="resourceName">Voller Ressourcenname, etwa <c>MdExplorer.App.Assets.Help.help.js</c>.</param>
    /// <exception cref="InvalidOperationException">Die Ressource fehlt in der Bibliothek.</exception>
    public static string ReadAsset(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        using Stream? stream = typeof(EmbeddedDocument).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Eingebettete Ressource fehlt: {resourceName}.");
        }
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Füllt die Vorlage: erst eine frische Nonce, dann die festen Teile, die Nutzlast zuletzt.</summary>
    /// <param name="template">Vorlage mit Platzhaltern.</param>
    /// <param name="parts">Teile der Anwendung, in dieser Reihenfolge eingesetzt.</param>
    /// <param name="payloadPlaceholder">Platzhalter der Nutzlast.</param>
    /// <param name="payload">Inhalt aus dem Bestand.</param>
    public static string Fill(
        string template,
        IReadOnlyList<KeyValuePair<string, string>> parts,
        string payloadPlaceholder,
        string payload)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadPlaceholder);
        ArgumentNullException.ThrowIfNull(payload);

        StringBuilder document = new StringBuilder(template).Replace(NoncePlaceholder, CreateNonce());
        foreach (KeyValuePair<string, string> part in parts)
        {
            _ = document.Replace(part.Key, part.Value);
        }
        return document.Replace(payloadPlaceholder, payload).ToString();
    }

    private static string CreateNonce()
    {
        Span<byte> buffer = stackalloc byte[NonceByteLength];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer);
    }
}
