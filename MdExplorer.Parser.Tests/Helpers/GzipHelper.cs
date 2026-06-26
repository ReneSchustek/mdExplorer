using System.IO.Compression;
using System.Text;

namespace MdExplorer.Parser.Tests.Helpers;

internal static class GzipHelper
{
    public static string Decompress(ReadOnlyMemory<byte> compressed)
    {
        using MemoryStream input = new(compressed.ToArray(), writable: false);
        using GZipStream gzip = new(input, CompressionMode.Decompress);
        using MemoryStream output = new();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
