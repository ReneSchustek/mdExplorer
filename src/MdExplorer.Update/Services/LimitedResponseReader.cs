using System.Net;

namespace MdExplorer.Update.Services;

/// <summary>
/// Liest kleine Antworten mit fester Obergrenze in den Speicher.
/// </summary>
/// <remarks>
/// <see cref="HttpClient.MaxResponseContentBufferSize"/> steht ab Werk auf 2 GB. Eine
/// Prüfsummen-Datei von einem Gigabyte landete damit vollständig im Speicher, obwohl der Inhalt
/// eine Zeile ist. Die Grenze gilt je Aufruf statt am Client, weil derselbe Client Antworten
/// sehr unterschiedlicher Größe holt.
/// </remarks>
internal static class LimitedResponseReader
{
    private const int ChunkSize = 4096;

    /// <summary>Liest den Körper der Antwort auf <paramref name="requestUri"/>.</summary>
    /// <param name="client">Der Client; relative Adressen gelten gegen seine Basisadresse.</param>
    /// <param name="requestUri">Angefragte Adresse.</param>
    /// <param name="maxBytes">Höchstzahl Byte des Körpers.</param>
    /// <param name="isTrustedSource">
    /// Prüft die Adresse nach allen Weiterleitungen; <see langword="null"/> verzichtet darauf.
    /// </param>
    /// <param name="cancellationToken">Abbruch.</param>
    /// <returns>Der vollständige Körper.</returns>
    /// <exception cref="HttpRequestException">
    /// Fehlerstatus, Körper über der Grenze oder Weiterleitung auf eine nicht zugelassene Adresse.
    /// </exception>
    public static async Task<byte[]> ReadAsync(
        HttpClient client,
        Uri requestUri,
        int maxBytes,
        Func<Uri, bool>? isTrustedSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        using HttpResponseMessage response = await client
            .GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        _ = response.EnsureSuccessStatusCode();
        ThrowIfUntrusted(response, isTrustedSource);
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw TooLarge(maxBytes);
        }

        Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (body.ConfigureAwait(false))
        {
            return await ReadBoundedAsync(body, maxBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Die Ausnahme für eine Antwort über der Grenze; auch der Download nutzt sie.</summary>
    internal static HttpRequestException TooLarge(long maxBytes) =>
        new(HttpRequestError.ConfigurationLimitExceeded, $"Die Antwort ist größer als {maxBytes} Byte.");

    /// <summary>Wirft, wenn die Adresse nach den Weiterleitungen nicht zugelassen ist.</summary>
    internal static void ThrowIfUntrusted(HttpResponseMessage response, Func<Uri, bool>? isTrustedSource)
    {
        if (isTrustedSource is null)
        {
            return;
        }

        // HttpClient setzt RequestMessage auf die Anfrage, die die Antwort tatsächlich geliefert
        // hat; nach einer Weiterleitung steht dort die neue Adresse.
        Uri? finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || !isTrustedSource(finalUri))
        {
            throw new HttpRequestException(
                HttpRequestError.Unknown,
                $"Weiterleitung auf eine nicht zugelassene Adresse: {finalUri?.Host ?? "(unbekannt)"}",
                inner: null,
                HttpStatusCode.Redirect);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream body, int maxBytes, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[ChunkSize];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            // Die Längenangabe im Kopf ist freiwillig; gezählt wird deshalb auch beim Lesen.
            if (buffer.Length + read > maxBytes)
            {
                throw TooLarge(maxBytes);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }
}
