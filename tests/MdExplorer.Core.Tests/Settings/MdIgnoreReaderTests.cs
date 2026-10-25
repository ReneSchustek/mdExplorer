using System.Text;
using MdExplorer.Core.Settings;
using MdExplorer.Core.Tests.Fakes;

namespace MdExplorer.Core.Tests.Settings;

/// <summary>
/// Aus einer <c>.mdignore</c> kommen nur echte Muster. Ein Kommentar oder eine Leerzeile als
/// Muster wäre wirkungslos, eine Byte-Order-Mark vor dem ersten Muster machte es unbrauchbar.
/// </summary>
public sealed class MdIgnoreReaderTests
{
    [Fact]
    public void Read_OnMissingFile_ReturnsEmpty()
    {
        InMemoryFileSystem fs = new();
        MdIgnoreReader sut = new(fs);

        IReadOnlyList<string> result = sut.Read(@"C:\root");

        Assert.Empty(result);
    }

    [Fact]
    public void Read_OnFileWithCommentsAndBlankLines_FiltersThemOut()
    {
        InMemoryFileSystem fs = new();
        fs.AddFile(@"C:\root\.mdignore",
            "# kommentar\n" +
            "\n" +
            "**/Drafts/**\n" +
            "  \n" +
            "!important.md\n");
        MdIgnoreReader sut = new(fs);

        IReadOnlyList<string> result = sut.Read(@"C:\root");

        Assert.Equal(["**/Drafts/**", "!important.md"], result);
    }

    [Fact]
    public void Read_OnUtf8BomFile_StripsBomBeforeParsing()
    {
        InMemoryFileSystem fs = new();
        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] payload = Encoding.UTF8.GetBytes("**/Trash/**\n");
        byte[] bytes = new byte[bom.Length + payload.Length];
        Buffer.BlockCopy(bom, 0, bytes, 0, bom.Length);
        Buffer.BlockCopy(payload, 0, bytes, bom.Length, payload.Length);
        fs.AddFile(@"C:\root\.mdignore", bytes);
        MdIgnoreReader sut = new(fs);

        IReadOnlyList<string> result = sut.Read(@"C:\root");

        Assert.Equal(["**/Trash/**"], result);
    }
}
