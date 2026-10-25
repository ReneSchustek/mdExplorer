using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Search.Tests.Integration;

/// <summary>
/// Ausschluss und offene Verknüpfungen gegen echtes FTS5. Eine Eingabe, die beim Tippen entsteht,
/// darf keine ungültige MATCH-Anfrage erzeugen: FTS5 kennt <c>NOT</c> nur zwischen zwei Ausdrücken
/// und weist eine Anfrage mit offenem Operator mit einem Syntaxfehler ab.
/// </summary>
public sealed class Fts5NegationIntegrationTests
{
    [Theory]
    [InlineData("bericht -entwurf", "final.md")]
    [InlineData("-entwurf bericht", "final.md")]
    [InlineData("bericht OR -entwurf", "final.md")]
    [InlineData("bericht OR notiz -entwurf", "final.md,notiz.md")]
    [InlineData("bericht OR", "entwurf.md,final.md")]
    [InlineData("bericht AND", "entwurf.md,final.md")]
    [InlineData("-entwurf", "")]
    [InlineData("-entwurf -final", "")]
    public async Task SearchAsync_OnNegationsAndOpenOperators_ReturnsTheExpectedFiles(string input, string expectedPaths)
    {
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            await SeedAsync(harness, new Guid("11111111-1111-1111-1111-111111111111"), "entwurf.md", "bericht entwurf").ConfigureAwait(true);
            await SeedAsync(harness, new Guid("22222222-2222-2222-2222-222222222222"), "final.md", "bericht final").ConfigureAwait(true);
            await SeedAsync(harness, new Guid("33333333-3333-3333-3333-333333333333"), "notiz.md", "notiz allein").ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            IReadOnlyList<SearchResult> results = await service
                .SearchAsync(new SearchQuery(input), TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            string actual = string.Join(',', results.Select(static result => result.Path).Order(StringComparer.Ordinal));
            Assert.Equal(expectedPaths, actual, StringComparer.Ordinal);
        }
    }

    private static Task SeedAsync(SearchTestHarness harness, Guid fileId, string fileName, string body) =>
        harness.SeedAsync(
            new SeedDocument(
                fileId,
                Title: Path.GetFileNameWithoutExtension(fileName),
                AbsolutePath: $@"C:\Wurzel\{fileName}",
                RelativePath: fileName,
                SourceContentHash: fileName,
                FrontmatterJson: "{}",
                Body: body,
                RawSource: body,
                Tags: []),
            TestContext.Current.CancellationToken);
}
