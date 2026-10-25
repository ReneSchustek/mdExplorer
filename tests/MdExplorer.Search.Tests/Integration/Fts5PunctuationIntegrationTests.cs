using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Search.Tests.Integration;

/// <summary>
/// Begriffe mit Satzzeichen gegen echtes FTS5. Der Tokenizer des Index (<c>unicode61</c>) zerlegt
/// „E-Mail“ in <c>e</c> und <c>mail</c>; eine Anfrage, die das Satzzeichen entfernt und
/// <c>email</c> sucht, findet deshalb nichts. Am schwersten wiegt das beim Klick in der
/// Tag-Cloud: Slugs tragen Bindestriche, und jedes mehrteilige Schlagwort ergäbe null Treffer.
/// </summary>
public sealed class Fts5PunctuationIntegrationTests
{
    private static readonly Guid MailFileId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ReleaseFileId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TaggedFileId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SluggedFileId = new("44444444-4444-4444-4444-444444444444");

    [Theory]
    [InlineData("e-mail", "mail.md")]
    [InlineData("E-Mail", "mail.md")]
    [InlineData("readme.md", "mail.md")]
    [InlineData("2026-10-24", "release.md")]
    [InlineData("v1.2", "release.md")]
    [InlineData("release-not*", "release.md")]
    [InlineData("tag:projekt-alpha", "projekt.md,slug.md")]
    [InlineData("tag:\"projekt alpha\"", "projekt.md,slug.md")]
    [InlineData("bericht -e-mail", "release.md")]
    public async Task SearchAsync_OnTermsWithPunctuation_FindsTheFiles(string input, string expectedPaths)
    {
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            await SeedAsync(harness, MailFileId, "mail.md", "bericht per E-Mail, siehe readme.md", []).ConfigureAwait(true);
            await SeedAsync(harness, ReleaseFileId, "release.md", "bericht release-notes v1.2 vom 2026-10-24", []).ConfigureAwait(true);
            await SeedAsync(harness, TaggedFileId, "projekt.md", "notiz", [("projekt-alpha", "Projekt Alpha")]).ConfigureAwait(true);
            await SeedAsync(harness, SluggedFileId, "slug.md", "notiz", [("projekt-alpha-2", "projekt-alpha")]).ConfigureAwait(true);
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

    private static Task SeedAsync(
        SearchTestHarness harness,
        Guid fileId,
        string fileName,
        string body,
        IReadOnlyList<(string Slug, string Name)> tags) =>
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
                Tags: tags),
            TestContext.Current.CancellationToken);
}
