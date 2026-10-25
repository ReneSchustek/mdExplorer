using System.Text;

namespace MdExplorer.Core.Text;

/// <summary>
/// Hilfsfunktionen für die UTF-8-Dekodierung roher Byte-Folgen.
/// </summary>
public static class Utf8Decoder
{
    // Wirft bei einer ungültigen Folge, statt sie still durch U+FFFD zu ersetzen.
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Dekodiert die Byte-Folge als UTF-8 und überspringt dabei eine eventuell
    /// vorhandene Byte-Order-Mark (<c>EF BB BF</c>) am Anfang.
    /// </summary>
    /// <remarks>
    /// Ungültige Folgen werden zu U+FFFD. Für den Index und die Vorschau ist das richtig: Eine
    /// Datei in fremder Kodierung soll lesbar erscheinen, nicht fehlen. Wer den Text zurück in die
    /// Datei schreibt, nimmt <see cref="TryDecodeStrict"/>; sonst zerstörte das Speichern jedes
    /// Zeichen, das nicht UTF-8 war.
    /// </remarks>
    /// <param name="bytes">Roh-Bytes, etwa aus <c>File.ReadAllBytes</c>.</param>
    /// <returns>UTF-8-Text ohne führende BOM.</returns>
    public static string DecodeNoBom(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Encoding.UTF8.GetString(WithoutBom(bytes));
    }

    /// <summary>
    /// Dekodiert die Byte-Folge nur, wenn sie gültiges UTF-8 ist.
    /// </summary>
    /// <param name="bytes">Roh-Bytes der Datei.</param>
    /// <param name="text">
    /// Der Text ohne BOM. Bei ungültigen Bytes trotzdem gefüllt, mit U+FFFD an den
    /// ungültigen Stellen, damit er angezeigt werden kann.
    /// </param>
    /// <returns><see langword="true"/>, wenn die Datei gültiges UTF-8 ist.</returns>
    public static bool TryDecodeStrict(byte[] bytes, out string text)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ReadOnlySpan<byte> content = WithoutBom(bytes);
        try
        {
            text = Strict.GetString(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.UTF8.GetString(content);
            return false;
        }
    }

    private static ReadOnlySpan<byte> WithoutBom(byte[] bytes)
    {
        ReadOnlySpan<byte> bom = Encoding.UTF8.Preamble;
        return bytes.AsSpan().StartsWith(bom) ? bytes.AsSpan(bom.Length) : bytes;
    }
}
