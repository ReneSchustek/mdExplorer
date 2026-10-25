using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Relative Bildpfade einer Notiz lösen sich gegen ihren eigenen Ordner auf, auch über
/// <c>..</c> in einen Nachbarordner, solange sie die Index-Wurzel nicht verlassen.
/// </summary>
/// <remarks>
/// Die Vorschau gibt dem Browser nur die Wurzel frei, unter einem virtuellen Rechnernamen. Führt
/// ein Pfad darüber hinaus, wird er nicht umgeschrieben und lädt nicht. Ließe man den Browser die
/// Adresse normalisieren, kappte er die überzähligen <c>..</c> an der Wurzel und zeigte eine
/// gleichnamige, aber falsche Datei.
/// </remarks>
public sealed class PreviewImageBaseTests
{
    private const string Host = "https://" + PreviewHtmlBuilder.ImageHost + "/";

    private static readonly PreviewImageBase InSubfolder = PreviewImageBase.ForFile(@"C:\notes\projekt\plan\doku.md", "projekt/plan/doku.md");

    [Theory]
    [InlineData("bild.png", "projekt/plan/bild.png")]
    [InlineData("./bild.png", "projekt/plan/bild.png")]
    [InlineData("../bilder/x.png", "projekt/bilder/x.png")]
    [InlineData("../../x.png", "x.png")]
    [InlineData(@"bilder\x.png", "projekt/plan/bilder/x.png")]
    [InlineData("/docs/x.png", "docs/x.png")]
    [InlineData("bild.png?raw=true#oben", "projekt/plan/bild.png?raw=true#oben")]
    public void Resolve_InsideTheRoot_PointsAtTheFileItNames(string source, string expected)
    {
        Assert.Equal(Host + expected, InSubfolder.Resolve(source));
    }

    [Theory]
    [InlineData("../../../x.png")]
    [InlineData("../../%2e%2e/x.png")]
    [InlineData("/../x.png")]
    public void Resolve_AboveTheRoot_ReturnsNothing(string source)
    {
        Assert.Null(InSubfolder.Resolve(source));
    }

    [Fact]
    public void ForFile_OfAnIndexedFile_MapsTheIndexRoot()
    {
        Assert.Equal(@"C:\notes", InSubfolder.Folder);
        Assert.Equal("projekt/plan", InSubfolder.RelativeDirectory);
    }

    /// <remarks>
    /// Eine Datei, die der Index nicht kennt, hat keine bekannte Wurzel. Freigegeben wird dann nur
    /// ihr eigener Ordner; was darüber liegt, bleibt der Vorschau verschlossen.
    /// </remarks>
    [Fact]
    public void ForFile_WithoutAnIndexEntry_MapsOnlyTheFilesOwnFolder()
    {
        PreviewImageBase loose = PreviewImageBase.ForFile(@"C:\Downloads\liesmich.md", relativePath: null);

        Assert.Equal(@"C:\Downloads", loose.Folder);
        Assert.Equal(Host + "bild.png", loose.Resolve("bild.png"));
        Assert.Null(loose.Resolve("../bild.png"));
    }

    [Fact]
    public void ForFile_WhenTheRelativePathDoesNotMatch_FallsBackToTheFilesFolder()
    {
        PreviewImageBase odd = PreviewImageBase.ForFile(@"C:\notes\a\doku.md", "anders/doku.md");

        Assert.Equal(@"C:\notes\a", odd.Folder);
        Assert.Equal(string.Empty, odd.RelativeDirectory);
    }
}
