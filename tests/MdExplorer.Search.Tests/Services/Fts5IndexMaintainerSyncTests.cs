using System.Data.Common;
using System.Text;
using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Options;
using MdExplorer.Search.Services;
using MdExplorer.Search.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// Was ein Abgleich der Indexpflege in den Index schreibt: Text ohne Frontmatter, Schlagworte,
/// Frontmatter-Spalte, Umgang mit nicht lesbaren und verschwundenen Dateien.
/// </summary>
public sealed class Fts5IndexMaintainerSyncTests
{
    private static readonly Guid IdA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid IdB = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task SynchronizeAsync_OnUnchangedIndex_ReportsNoChangesAndSkipsWriting()
    {
        LoopHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            harness.Source.Documents.Add(MaintainerDocuments.Document(IdA, "hash-A", @"C:\notizen\a.md"));
            harness.Files.SetContent(@"C:\notizen\a.md", "Inhalt A");
            _ = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            int writeCount = harness.Storage.ApplyCallCount;

            int changed = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(0, changed);
            Assert.Equal(writeCount, harness.Storage.ApplyCallCount);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_OnNewDocument_WritesBodyWithoutFrontmatter()
    {
        LoopHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            harness.Source.Documents.Add(MaintainerDocuments.Document(IdA, "hash-A", @"C:\notizen\a.md"));
            harness.Files.SetContent(@"C:\notizen\a.md", "---\ntitle: Bericht\n---\nDer eigentliche Text.");

            int changed = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(1, changed);
            SearchIndexEntry entry = Assert.Single(harness.Storage.Entries);
            // Der Frontmatter-Block gehört in die eigene Spalte, nicht in den Fließtext,
            // sonst treffen Suchen nach "title" jedes Dokument.
            Assert.Equal("Der eigentliche Text.", entry.Body, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WithTags_JoinsThemIntoOneColumn()
    {
        LoopHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            harness.Source.Documents.Add(MaintainerDocuments.Document(IdA, "hash-A", @"C:\notizen\a.md"));
            harness.Source.TagsById[IdA] = ["bericht", "quartal"];
            harness.Files.SetContent(@"C:\notizen\a.md", "Text");

            _ = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            SearchIndexEntry entry = Assert.Single(harness.Storage.Entries);
            Assert.Equal("bericht quartal", entry.Tags, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WithoutTags_LeavesTheTagColumnEmpty()
    {
        LoopHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            harness.Source.Documents.Add(MaintainerDocuments.Document(IdA, "hash-A", @"C:\notizen\a.md"));
            harness.Files.SetContent(@"C:\notizen\a.md", "Text");

            _ = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(string.Empty, Assert.Single(harness.Storage.Entries).Tags, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WithEmptyTagList_LeavesTheTagColumnEmpty()
    {
        LoopHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            harness.Source.Documents.Add(MaintainerDocuments.Document(IdA, "hash-A", @"C:\notizen\a.md"));
            harness.Source.TagsById[IdA] = [];
            harness.Files.SetContent(@"C:\notizen\a.md", "Text");

            _ = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(string.Empty, Assert.Single(harness.Storage.Entries).Tags, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WithFrontmatterJson_FlattensItIntoItsOwnColumn()
    {
        LoopHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            harness.Source.Documents.Add(MaintainerDocuments.Document(IdA, "hash-A", @"C:\notizen\a.md", """{"title":"Bericht"}"""));
            harness.Files.SetContent(@"C:\notizen\a.md", "Text");

            _ = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            SearchIndexEntry entry = Assert.Single(harness.Storage.Entries);
            Assert.Contains("Bericht", entry.Frontmatter, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WhenTheFileCannotBeRead_IndexesTheDocumentWithAnEmptyBody()
    {
        // Eine gesperrte Datei darf den ganzen Durchlauf nicht abbrechen: Titel, Tags und
        // Pfad bleiben durchsuchbar, nur der Fließtext fehlt.
        LoopHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            harness.Source.Documents.Add(MaintainerDocuments.Document(IdA, "hash-A", @"C:\notizen\gesperrt.md"));
            harness.Files.FailWith(@"C:\notizen\gesperrt.md", new IOException("Datei gesperrt"));

            int changed = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(1, changed);
            SearchIndexEntry entry = Assert.Single(harness.Storage.Entries);
            Assert.Equal(string.Empty, entry.Body, StringComparer.Ordinal);
            Assert.Equal("titel", entry.Title, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WhenTheFileIsMissing_IndexesTheDocumentWithAnEmptyBody()
    {
        LoopHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            harness.Source.Documents.Add(MaintainerDocuments.Document(IdA, "hash-A", @"C:\notizen\fehlt.md"));
            harness.Files.FailWith(@"C:\notizen\fehlt.md", new FileNotFoundException("weg"));

            _ = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(string.Empty, Assert.Single(harness.Storage.Entries).Body, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WhenAnUnreadableFileBecomesReadable_IndexesItsBodyAfterTheRetryDelay()
    {
        // Eine Sperre endet, ohne dass sich der Inhalt ändert. Der Text muss trotzdem in den
        // Index, sonst bliebe die Datei bis zur nächsten Bearbeitung ohne Volltext.
        LoopHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            harness.Source.Documents.Add(MaintainerDocuments.Document(IdA, "hash-A", @"C:\notizen\gesperrt.md"));
            harness.Files.FailWith(@"C:\notizen\gesperrt.md", new IOException("Datei gesperrt"));
            _ = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            harness.Files.SetContent(@"C:\notizen\gesperrt.md", "Jetzt lesbar");
            harness.AdvanceTime(TimeSpan.FromMinutes(10));
            _ = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal("Jetzt lesbar", Assert.Single(harness.Storage.Entries).Body, StringComparer.Ordinal);
            Assert.Equal("hash-A", harness.Storage.Indexed[IdA], StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_RightAfterAReadFailure_DoesNotTryTheFileAgain()
    {
        // Ohne Wartezeit läse jeder Takt die gesperrte Datei erneut, alle fünf Sekunden.
        LoopHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            harness.Source.Documents.Add(MaintainerDocuments.Document(IdA, "hash-A", @"C:\notizen\gesperrt.md"));
            harness.Files.FailWith(@"C:\notizen\gesperrt.md", new IOException("Datei gesperrt"));
            _ = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            int readsAfterFirstRun = harness.Files.ReadCount;

            harness.AdvanceTime(TimeSpan.FromSeconds(5));
            int changed = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(0, changed);
            Assert.Equal(readsAfterFirstRun, harness.Files.ReadCount);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WhileAnUnreadableFileWaits_IndexesTheOtherDocuments()
    {
        // Eine wartende Datei belegt keinen Platz im Stapel. Sonst hielten genug gesperrte
        // Dateien jeden anderen Eintrag dauerhaft aus dem Index.
        LoopHarness harness = new(failure: null, batchSize: 1);
        await using (harness.ConfigureAwait(true))
        {
            harness.Source.Documents.Add(MaintainerDocuments.Document(IdA, "hash-A", @"C:\notizen\gesperrt.md"));
            harness.Source.Documents.Add(MaintainerDocuments.Document(IdB, "hash-B", @"C:\notizen\b.md"));
            harness.Files.FailWith(@"C:\notizen\gesperrt.md", new IOException("Datei gesperrt"));
            harness.Files.SetContent(@"C:\notizen\b.md", "B");
            _ = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            _ = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal("hash-B", harness.Storage.Indexed[IdB], StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_OnARepeatedReadFailure_WritesTheStackTraceOnlyOnce()
    {
        LoopHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            harness.Source.Documents.Add(MaintainerDocuments.Document(IdA, "hash-A", @"C:\notizen\gesperrt.md"));
            harness.Files.FailWith(@"C:\notizen\gesperrt.md", new IOException("Datei gesperrt"));

            for (int run = 0; run < 3; run++)
            {
                _ = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
                harness.AdvanceTime(TimeSpan.FromMinutes(10));
            }

            Assert.Equal(3, harness.Files.ReadCount);
            Assert.Equal(1, harness.Logger.WarningsWithException);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WhenADocumentDisappears_RemovesItFromTheIndex()
    {
        LoopHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            harness.Source.Documents.Add(MaintainerDocuments.Document(IdA, "hash-A", @"C:\notizen\a.md"));
            harness.Source.Documents.Add(MaintainerDocuments.Document(IdB, "hash-B", @"C:\notizen\b.md"));
            harness.Files.SetContent(@"C:\notizen\a.md", "A");
            harness.Files.SetContent(@"C:\notizen\b.md", "B");
            _ = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            _ = harness.Source.Documents.RemoveAll(d => d.MarkdownFileId == IdB);
            int changed = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(1, changed);
            Assert.Contains(IdA, harness.Storage.Indexed.Keys);
            Assert.DoesNotContain(IdB, harness.Storage.Indexed.Keys);
        }
    }
}
