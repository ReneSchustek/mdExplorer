using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Ein Pfad von außen kommt aus der Befehlszeile oder von einer zweiten Instanz. Was davon nicht
/// geöffnet werden kann, wird mit Grund abgewiesen statt still verworfen.
/// </summary>
public sealed class MarkdownPathValidatorTests
{
    private const string WorkingDirectory = @"C:\Arbeit";
    private const string NotePath = @"C:\Notizen\Plan.md";

    private readonly FakeFileSystem _fileSystem = new();

    public MarkdownPathValidatorTests()
    {
        _fileSystem.Files[NotePath] = [];
    }

    [Fact]
    public void CheckCommandLine_WithoutArguments_HasNothingToOpen()
    {
        MarkdownPathCheck check = CreateSut().CheckCommandLine([], WorkingDirectory);

        Assert.True(check.IsNone);
        Assert.Same(MarkdownPathCheck.None, check);
    }

    [Fact]
    public void CheckCommandLine_WithAnExistingMarkdownFile_AcceptsTheFullPath()
    {
        MarkdownPathCheck check = CreateSut().CheckCommandLine([NotePath], WorkingDirectory);

        Assert.Equal(NotePath, check.AbsolutePath, StringComparer.Ordinal);
        Assert.Null(check.Problem);
        Assert.False(check.IsNone);
    }

    [Fact]
    public void CheckCommandLine_UsesOnlyTheFirstArgument()
    {
        // Mehrere Dateien auf einmal öffnet das Programm nicht; die erste gewinnt.
        MarkdownPathCheck check = CreateSut().CheckCommandLine([NotePath, @"C:\Notizen\Fehlt.md"], WorkingDirectory);

        Assert.Equal(NotePath, check.AbsolutePath, StringComparer.Ordinal);
    }

    [Fact]
    public void CheckCommandLine_WithAMissingFile_RejectsItAsNotFound()
    {
        MarkdownPathCheck check = CreateSut().CheckCommandLine([@"C:\Notizen\Fehlt.md"], WorkingDirectory);

        Assert.Equal(MarkdownPathProblem.NotFound, check.Problem);
        Assert.Equal(@"C:\Notizen\Fehlt.md", check.RejectedValue, StringComparer.Ordinal);
        Assert.Null(check.AbsolutePath);
    }

    [Theory]
    [InlineData(@"C:\Notizen\Plan.txt")]
    [InlineData(@"C:\Notizen\Plan.md.exe")]
    [InlineData(@"C:\Notizen\Plan")]
    public void CheckCommandLine_WithAnotherExtension_RejectsItWithoutTouchingTheDisk(string path)
    {
        _fileSystem.Files[path] = [];

        MarkdownPathCheck check = CreateSut().CheckCommandLine([path], WorkingDirectory);

        Assert.Equal(MarkdownPathProblem.UnsupportedExtension, check.Problem);
        Assert.Equal(path, check.RejectedValue, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\Notizen\Plan.MD")]
    [InlineData(@"C:\Notizen\Plan.markdown")]
    [InlineData(@"C:\Notizen\Plan.Markdown")]
    public void CheckCommandLine_AcceptsBothExtensionsInAnyCase(string path)
    {
        _fileSystem.Files[path] = [];

        MarkdownPathCheck check = CreateSut().CheckCommandLine([path], WorkingDirectory);

        Assert.Equal(path, check.AbsolutePath, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("\"C:\\Notizen\\Plan.md\"")]
    [InlineData("  C:\\Notizen\\Plan.md  ")]
    [InlineData("C:\\Notizen\\Plan.md\"")]
    [InlineData("\" C:\\Notizen\\Plan.md \"")]
    public void CheckCommandLine_StripsQuotesAndSurroundingBlanks(string argument)
    {
        MarkdownPathCheck check = CreateSut().CheckCommandLine([argument], WorkingDirectory);

        Assert.Equal(NotePath, check.AbsolutePath, StringComparer.Ordinal);
    }

    [Fact]
    public void CheckCommandLine_ResolvesARelativePathAgainstTheWorkingDirectory()
    {
        _fileSystem.Files[@"C:\Arbeit\Unterordner\Liste.md"] = [];

        MarkdownPathCheck check = CreateSut().CheckCommandLine([@"Unterordner\..\Unterordner\Liste.md"], WorkingDirectory);

        Assert.Equal(@"C:\Arbeit\Unterordner\Liste.md", check.AbsolutePath, StringComparer.Ordinal);
    }

    [Fact]
    public void CheckCommandLine_NormalizesDotSegmentsOfAFullPath()
    {
        MarkdownPathCheck check = CreateSut().CheckCommandLine([@"C:\Notizen\.\Archiv\..\Plan.md"], WorkingDirectory);

        Assert.Equal(NotePath, check.AbsolutePath, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    public void CheckCommandLine_WithAnEmptyArgument_RejectsItAsInvalid(string argument)
    {
        MarkdownPathCheck check = CreateSut().CheckCommandLine([argument], WorkingDirectory);

        Assert.Equal(MarkdownPathProblem.InvalidPath, check.Problem);
    }

    [Fact]
    public void CheckCommandLine_WithANullCharacter_RejectsItAsInvalid()
    {
        MarkdownPathCheck check = CreateSut().CheckCommandLine(["C:\\Notizen\\P\0lan.md"], WorkingDirectory);

        Assert.Equal(MarkdownPathProblem.InvalidPath, check.Problem);
    }

    [Fact]
    public void CheckForwarded_WithoutAPath_OnlyAsksForTheWindow()
    {
        Assert.Same(MarkdownPathCheck.None, CreateSut().CheckForwarded(null));
    }

    [Fact]
    public void CheckForwarded_WithAFullPath_AcceptsIt()
    {
        MarkdownPathCheck check = CreateSut().CheckForwarded(NotePath);

        Assert.Equal(NotePath, check.AbsolutePath, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("Plan.md")]
    [InlineData(@"\Notizen\Plan.md")]
    [InlineData(@"C:Plan.md")]
    public void CheckForwarded_WithAPathThatIsNotFullyQualified_RejectsIt(string path)
    {
        // Das Arbeitsverzeichnis der anderen Instanz ist hier unbekannt; gegen das eigene
        // aufgelöst, zeigte der Pfad womöglich auf eine ganz andere Datei.
        MarkdownPathCheck check = CreateSut().CheckForwarded(path);

        Assert.Equal(MarkdownPathProblem.InvalidPath, check.Problem);
        Assert.Equal(path, check.RejectedValue, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(@"\\?\C:\Notizen\Plan.md")]
    [InlineData(@"\\.\pipe\Plan.md")]
    [InlineData(@"\\.\C:\Notizen\Plan.md")]
    [InlineData(@"C:\Notizen\Plan.txt:notiz.md")]
    [InlineData(@"C:\Notizen\Plan.md:Zone.Identifier.md")]
    public void CheckForwarded_WithAPathThatIsNoOrdinaryFile_IsRejected(string path)
    {
        // Ein Gerätepfad liest aus einem Kanal oder einem ganzen Laufwerk, ein Datenstrom schreibt
        // beim Speichern unsichtbar neben eine andere Datei. Beide enden auf .md.
        _fileSystem.Files[path] = [];

        MarkdownPathCheck result = CreateSut().CheckForwarded(path);

        Assert.Equal(MarkdownPathProblem.InvalidPath, result.Problem);
        Assert.Null(result.AbsolutePath);
    }

    [Fact]
    public void CheckForwarded_WithAPathOnANetworkShare_AcceptsIt()
    {
        const string sharePath = @"\\nas\notizen\Plan.md";
        _fileSystem.Files[sharePath] = [];

        MarkdownPathCheck result = CreateSut().CheckForwarded(sharePath);

        Assert.Equal(sharePath, result.AbsolutePath, StringComparer.Ordinal);
    }

    [Fact]
    public void Constructor_WithoutFileSystem_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new MarkdownPathValidator(null!));
    }

    [Fact]
    public void CheckCommandLine_WithoutWorkingDirectory_Throws()
    {
        _ = Assert.Throws<ArgumentException>(() => CreateSut().CheckCommandLine([NotePath], " "));
    }

    private MarkdownPathValidator CreateSut() => new(_fileSystem);
}
