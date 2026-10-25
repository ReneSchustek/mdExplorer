using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace MdExplorer.App.Hosting;

/// <summary>
/// Das Format zwischen zwei Instanzen: vier Bytes Länge, dann der Pfad in UTF-8; die Antwort ist
/// ein einzelnes Byte.
/// </summary>
/// <remarks>
/// Länge null heißt: keine Datei, nur das Fenster nach vorn holen. Die Antwort sagt der zweiten
/// Instanz, dass die laufende den Auftrag angenommen hat; ohne sie wüsste sie nicht, ob sie eine
/// Fehlermeldung zeigen muss.
/// </remarks>
internal static class InstanceMessageCodec
{
    /// <summary>
    /// Obergrenze für den Pfad. Windows erlaubt höchstens 32.767 UTF-16-Zeichen, in UTF-8 also
    /// knapp 96 KiB; was darüber angekündigt wird, ist kein Pfad.
    /// </summary>
    public const int MaxPayloadBytes = 96 * 1024;

    private const int HeaderBytes = sizeof(int);
    private const byte Acknowledged = 0x06;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Schreibt einen Auftrag.</summary>
    /// <param name="stream">Der Kanal.</param>
    /// <param name="absolutePath">Die Datei; <see langword="null"/>, wenn nur das Fenster nach vorn soll.</param>
    /// <param name="cancellationToken">Abbruch.</param>
    public static async Task WriteRequestAsync(Stream stream, string? absolutePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] payload = absolutePath is null ? [] : StrictUtf8.GetBytes(absolutePath);
        if (payload.Length > MaxPayloadBytes)
        {
            throw new ArgumentException("Der Pfad ist zu lang für eine Übergabe.", nameof(absolutePath));
        }

        byte[] header = new byte[HeaderBytes];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Liest einen Auftrag.</summary>
    /// <param name="stream">Der Kanal.</param>
    /// <param name="cancellationToken">Abbruch.</param>
    /// <returns>Der Pfad, oder <see langword="null"/>, wenn nur das Fenster nach vorn soll.</returns>
    /// <exception cref="InvalidDataException">Die Länge ist unzulässig oder der Text kein UTF-8.</exception>
    /// <exception cref="EndOfStreamException">Der Kanal endete vor dem Ende des Auftrags.</exception>
    public static async Task<string?> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] header = new byte[HeaderBytes];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 0 or > MaxPayloadBytes)
        {
            throw new InvalidDataException($"Unzulässige Länge {length} im Auftrag.");
        }
        if (length == 0)
        {
            return null;
        }

        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        try
        {
            return StrictUtf8.GetString(payload);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Der Auftrag ist kein gültiges UTF-8.", exception);
        }
    }

    /// <summary>Bestätigt den Empfang.</summary>
    /// <param name="stream">Der Kanal.</param>
    /// <param name="cancellationToken">Abbruch.</param>
    public static async Task WriteAcknowledgementAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        await stream.WriteAsync(new[] { Acknowledged }, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Wartet auf die Empfangsbestätigung.</summary>
    /// <param name="stream">Der Kanal.</param>
    /// <param name="cancellationToken">Abbruch.</param>
    /// <exception cref="InvalidDataException">Die Antwort ist keine Bestätigung.</exception>
    /// <exception cref="EndOfStreamException">Der Kanal endete ohne Antwort.</exception>
    public static async Task ReadAcknowledgementAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] answer = new byte[1];
        await stream.ReadExactlyAsync(answer, cancellationToken).ConfigureAwait(false);
        if (answer[0] != Acknowledged)
        {
            throw new InvalidDataException($"Unerwartete Antwort {answer[0]} statt Bestätigung.");
        }
    }
}
