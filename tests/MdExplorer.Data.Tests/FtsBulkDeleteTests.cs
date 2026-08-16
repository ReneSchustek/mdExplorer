using MdExplorer.Core.Abstractions;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Prüft, dass der Volltext-Index in einem Zug aufräumt und ohne Auslöser auskommt.
/// </summary>
/// <remarks>
/// <para>
/// <c>MarkdownFileId</c> ist in der FTS5-Tabelle <c>UNINDEXED</c>. Jede Bedingung darauf
/// liest die ganze Tabelle — einzeln gelöscht kostete das am 16.08.2026 rund 170 ms je
/// Datei, also über eine Stunde für die 25.000 Einträge, die nach dem Setzen der
/// Ausschlussmuster wegfielen. Dieselben 200 Dateien in einem Zug: 0,3 Sekunden.
/// </para>
/// <para>
/// Die beiden Aufräum-Auslöser sind deshalb weg. Was sie sicherten, sichert der
/// Volltext-Abgleich alle fünf Sekunden — dieselbe Frist, die fürs Hinzufügen ohnehin gilt.
/// </para>
/// </remarks>
public sealed class FtsBulkDeleteTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MdExplorerDbContext _dbContext;
    private readonly SqliteSearchIndexStorage _sut;

    public FtsBulkDeleteTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<MdExplorerDbContext> options = new DbContextOptionsBuilder<MdExplorerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        _dbContext = new MdExplorerDbContext(options);
        _dbContext.Database.Migrate();
        _sut = new SqliteSearchIndexStorage(_dbContext);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task Migrations_LeaveNoFtsCleanupTriggers()
    {
        List<string> triggers = await TriggerNamesAsync().ConfigureAwait(true);

        Assert.DoesNotContain("trg_MarkdownFiles_AD_FtsCleanup", triggers);
        Assert.DoesNotContain("trg_MarkdownDocuments_AD_FtsCleanup", triggers);
    }

    [Fact]
    public async Task ApplyChangesAsync_RemovesEveryRequestedEntry()
    {
        Guid[] ids = [.. Enumerable.Range(0, 30).Select(_ => Guid.NewGuid())];
        await _sut.ApplyChangesAsync(
            [],
            [.. ids.Select(id => EntryFor(id))],
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        IReadOnlyDictionary<Guid, string> before =
            await _sut.LoadIndexedHashesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(30, before.Count);

        await _sut.ApplyChangesAsync(ids[..20], [], TestContext.Current.CancellationToken).ConfigureAwait(true);

        IReadOnlyDictionary<Guid, string> after =
            await _sut.LoadIndexedHashesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(10, after.Count);
        Assert.All(ids[..20], id => Assert.False(after.ContainsKey(id)));
        Assert.All(ids[20..], id => Assert.True(after.ContainsKey(id)));
    }

    [Fact]
    public async Task ApplyChangesAsync_OnUpsertOfKnownFile_KeepsOneEntry()
    {
        // Das Ersetzen läuft über dieselbe Sammel-Löschung. Bliebe die alte Zeile stehen,
        // stünde derselbe Treffer zweimal in der Ergebnisliste.
        Guid id = Guid.NewGuid();
        await _sut.ApplyChangesAsync([], [EntryFor(id)], TestContext.Current.CancellationToken).ConfigureAwait(true);
        await _sut.ApplyChangesAsync([], [EntryFor(id, "zweiter")], TestContext.Current.CancellationToken).ConfigureAwait(true);

        IReadOnlyDictionary<Guid, string> state =
            await _sut.LoadIndexedHashesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal("hash-zweiter", Assert.Contains(id, state));
        _ = Assert.Single(state);
    }

    [Fact]
    public async Task ApplyChangesAsync_OverMoreThanOnePortion_RemovesAll()
    {
        // Mehr als die Portionsgröße von 500 — die Schleife über die Portionen darf keine
        // Datei unterschlagen.
        Guid[] ids = [.. Enumerable.Range(0, 620).Select(_ => Guid.NewGuid())];
        await _sut.ApplyChangesAsync(
            [],
            [.. ids.Select(id => EntryFor(id))],
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        await _sut.ApplyChangesAsync(ids, [], TestContext.Current.CancellationToken).ConfigureAwait(true);

        IReadOnlyDictionary<Guid, string> after =
            await _sut.LoadIndexedHashesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Empty(after);
    }

    private static SearchIndexEntry EntryFor(Guid id, string suffix = "erster") =>
        new(
            id,
            "Titel " + suffix,
            "Rumpf " + suffix,
            string.Empty,
            string.Empty,
            @"C:\notes\" + id.ToString("N") + ".md",
            "hash-" + suffix);

    private async Task<List<string>> TriggerNamesAsync()
    {
        List<string> names = [];
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'trigger';";
        using SqliteDataReader reader = await command
            .ExecuteReaderAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken).ConfigureAwait(true))
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }
}
