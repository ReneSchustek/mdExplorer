using System.IO;
using System.IO.Compression;
using System.Text;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>Komprimiert HTML so, wie der Parser es in <c>RenderedHtmlGz</c> ablegt.</summary>
internal static class TestGzip
{
    public static byte[] Compress(string text)
    {
        using MemoryStream output = new();
        using (GZipStream gzip = new(output, CompressionLevel.Fastest))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            gzip.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }
}
