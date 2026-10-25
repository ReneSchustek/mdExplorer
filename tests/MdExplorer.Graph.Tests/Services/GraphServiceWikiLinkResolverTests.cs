using MdExplorer.Core.Abstractions;
using MdExplorer.Graph.Options;
using MdExplorer.Graph.Services;
using MdExplorer.Graph.Tests.Fakes;
using MdExplorer.Parser.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Graph.Tests.Services;

/// <summary>
/// Ein Klick auf einen WikiLink öffnet dieselbe Datei, auf die im Graphen die Kante zeigt.
/// </summary>
/// <remarks>
/// Die Vorschau gibt das Ziel als Slug weiter (<c>projekt-x</c>), der Dateiname lautet
/// <c>Projekt X</c>. Ein Vergleich mit dem Dateinamen fände die Datei nie, sobald der Name ein
/// Leerzeichen oder ein Satzzeichen enthält.
/// </remarks>
public sealed class GraphServiceWikiLinkResolverTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid LaterPathId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EarlierPathId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData("projekt-x")]
    [InlineData("Projekt X")]
    [InlineData("  projekt x ")]
    public async Task ResolveAsync_FindsTheFileBySlugOrByName(string target)
    {
        GraphService sut = Build([new GraphSourceFile(ProjectId, "Projekt X", "Projekt X.md")]);

        Guid? resolved = await sut.ResolveAsync(target, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(ProjectId, resolved);
    }

    [Fact]
    public async Task ResolveAsync_WithTwoFilesOfTheSameName_TakesTheFirstByRelativePath()
    {
        GraphService sut = Build(
        [
            new GraphSourceFile(LaterPathId, "Notiz", "z/Notiz.md"),
            new GraphSourceFile(EarlierPathId, "Notiz", "a/Notiz.md"),
        ]);

        Guid? resolved = await sut.ResolveAsync("notiz", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(EarlierPathId, resolved);
    }

    [Theory]
    [InlineData("gibt-es-nicht")]
    [InlineData("+")]
    public async Task ResolveAsync_WithoutAMatchingFile_ReturnsNull(string target)
    {
        GraphService sut = Build([new GraphSourceFile(ProjectId, "Projekt X", "Projekt X.md")]);

        Guid? resolved = await sut.ResolveAsync(target, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Null(resolved);
    }

    private static GraphService Build(IReadOnlyList<GraphSourceFile> files) =>
        new(
            new FakeGraphSourceProvider(new GraphSourceData(files, [])),
            new TagNormalizer(),
            Microsoft.Extensions.Options.Options.Create(new GraphOptions()),
            NullLogger<GraphService>.Instance);
}
