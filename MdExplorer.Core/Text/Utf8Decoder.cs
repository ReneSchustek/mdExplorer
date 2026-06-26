using System.Text;

namespace MdExplorer.Core.Text;

/// <summary>
/// Hilfsfunktionen für die UTF-8-Dekodierung roher Byte-Folgen.
/// </summary>
public static class Utf8Decoder
{
    /// <summary>
    /// Dekodiert die Byte-Folge als UTF-8 und überspringt dabei eine eventuell
    /// vorhandene Byte-Order-Mark (<c>EF BB BF</c>) am Anfang.
    /// </summary>
    /// <param name="bytes">Roh-Bytes, etwa aus <c>File.ReadAllBytes</c>.</param>
    /// <returns>UTF-8-Text ohne führende BOM.</returns>
    public static string DecodeNoBom(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        return Encoding.UTF8.GetString(bytes);
    }
}
