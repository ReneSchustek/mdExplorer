using System.Text;

namespace MdExplorer.Core.Text;

/// <summary>
/// Hilfsfunktionen für die UTF-8-Dekodierung roher Byte-Folgen.
/// </summary>
public static class Utf8Decoder
{
    /// <summary>Die UTF-8-Byte-Order-Mark (<c>EF BB BF</c>).</summary>
    // Die drei Bytes bilden gemeinsam die benannte Konstante Utf8Bom (Unicode-Standard-Sequenz).
    // Eine Aufspaltung in Einzel-Byte-Konstanten wäre Rausch-Doku (deep-quality Kap. 5).
#pragma warning disable S109 // Standardisierte UTF-8-BOM-Bytes, gemeinsam als Utf8Bom benannt.
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];
#pragma warning restore S109

    /// <summary>
    /// Dekodiert die Byte-Folge als UTF-8 und überspringt dabei eine eventuell
    /// vorhandene Byte-Order-Mark (<c>EF BB BF</c>) am Anfang.
    /// </summary>
    /// <param name="bytes">Roh-Bytes, etwa aus <c>File.ReadAllBytes</c>.</param>
    /// <returns>UTF-8-Text ohne führende BOM.</returns>
    public static string DecodeNoBom(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.AsSpan().StartsWith(Utf8Bom))
        {
            return Encoding.UTF8.GetString(bytes, Utf8Bom.Length, bytes.Length - Utf8Bom.Length);
        }
        return Encoding.UTF8.GetString(bytes);
    }
}
