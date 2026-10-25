using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Search.Tests.Integration;

/// <summary>
/// Die RegEx-Suche gegen echtes FTS5. Der Vorfilter fragt FTS5 nach Wortanfängen; stimmen die
/// nicht mit dem Muster überein, fehlen Treffer ohne Hinweis. Aus <c>\bTODO\b</c> wurde früher
/// <c>"bTODO"*</c>, weil das <c>b</c> der Wortgrenze am Wort hing; der Vorfilter fand dann nichts.
/// </summary>
public sealed class Fts5RegexIntegrationTests
{
    private static readonly Guid TodoFileId = new("61111111-1111-1111-1111-111111111111");
    private static readonly Guid InvoiceFileId = new("62222222-2222-2222-2222-222222222222");
    private static readonly Guid SettlementFileId = new("63333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData(@"\bTODO\b", "todo.md")]
    [InlineData(@"\bRechnung\s+\d+", "rechnung.md")]
    [InlineData(@"\bRechnung", "rechnung.md")]
    [InlineData(@"\babrechnung", "abrechnung.md")]
    [InlineData("ment", "")] // ohne gesicherten Wortanfang abgelehnt, nicht halb beantwortet
    public async Task SearchAsync_InRegexMode_FindsExactlyTheMatchingFiles(string pattern, string expectedPaths)
    {
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            await SeedAsync(harness, TodoFileId, "todo.md", "TODO: Dokument fertigstellen").ConfigureAwait(true);
            await SeedAsync(harness, InvoiceFileId, "rechnung.md", "Rechnung 4711 ist bezahlt").ConfigureAwait(true);
            await SeedAsync(harness, SettlementFileId, "abrechnung.md", "Die Abrechnung folgt").ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            ISearchService service = harness.CreateSearchService(scope);
            IReadOnlyList<SearchResult> results = await service
                .SearchAsync(new SearchQuery(pattern, Mode: SearchMode.Regex), TestContext.Current.CancellationToken)
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
