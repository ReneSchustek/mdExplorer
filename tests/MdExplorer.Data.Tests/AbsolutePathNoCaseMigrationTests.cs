using MdExplorer.Data;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Verifiziert die DB-Kompatibilität der NOCASE-Collation-Migration: eine bestehende Datenbank
/// (migriert bis zur Vorgänger-Migration, mit Daten befüllt) muss sauber auf den neuen Stand
/// wandern — Zeilen bleiben erhalten, der Lookup wird case-insensitiv und der beim Tabellen-Rebuild
/// gedroppte FTS5-Cleanup-Trigger auf <c>MarkdownFiles</c> ist danach wieder vorhanden.
/// </summary>
public sealed class AbsolutePathNoCaseMigrationTests : IAsyncDisposable
{
    // Migration unmittelbar vor AddAbsolutePathNoCaseCollation.
    private const string PreviousMigration = "20260609172327_RestoreFtsCleanupTriggersAfterRebuild";
    private const string FileTriggerName = "trg_MarkdownFiles_AD_FtsCleanup";

    // Der Stand, auf dem der Auslöser zuletzt existierte. Danach fällt er weg — die
    // Begründung steht in der Migration DropFtsCleanupTriggers.
    private const string TriggerRestoredMigration = "20260722051331_RestoreFileTriggerAfterNoCaseRebuild";

    private readonly SqliteConnection _connection;
    private readonly MdExplorerDbContext _dbContext;

    public AbsolutePathNoCaseMigrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<MdExplorerDbContext> options = new DbContextOptionsBuilder<MdExplorerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        _dbContext = new MdExplorerDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task Migrate_FromPreviousSchemaWithData_PreservesRowsAndAppliesNoCaseAndRestoresTrigger()
    {
        IMigrator migrator = _dbContext.GetInfrastructure().GetRequiredService<IMigrator>();

        // 1. Bestehende DB bis zur Vorgänger-Migration aufbauen und mit einer Zeile befüllen.
        await migrator.MigrateAsync(PreviousMigration, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Guid fileId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
        await InsertMarkdownFileAsync(fileId, @"C:\Notes\MixedCase.md").ConfigureAwait(true);

        // 2. Bis zu dem Stand migrieren, auf dem der Auslöser zuletzt existierte.
        await migrator.MigrateAsync(TriggerRestoredMigration, TestContext.Current.CancellationToken).ConfigureAwait(true);

        // 3. Zeile blieb erhalten und ist per abweichender Casing auffindbar (NOCASE-Spalte).
        Guid? foundByLowerCase = await FindIdByPathAsync(@"c:\notes\mixedcase.md").ConfigureAwait(true);
        Assert.Equal(fileId, foundByLowerCase);

        // 4. Der beim Rebuild gedroppte File-Cleanup-Auslöser war dort wiederhergestellt.
        Assert.True(await TriggerExistsAsync(FileTriggerName).ConfigureAwait(true));

        // 5. Auf den neuesten Stand: Der Auslöser ist weg, die Zeile bleibt. Erstes Argument
        // ist das Ziel der Migration — null heißt „bis zum neuesten Stand".
        await migrator.MigrateAsync(null, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.False(await TriggerExistsAsync(FileTriggerName).ConfigureAwait(true));
        Assert.Equal(fileId, await FindIdByPathAsync(@"c:\notes\mixedcase.md").ConfigureAwait(true));
    }

    private async Task InsertMarkdownFileAsync(Guid id, string absolutePath)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "MarkdownFiles"
                ("Id", "AbsolutePath", "RelativePath", "FileNameWithoutExtension",
                 "SizeBytes", "LastWriteTimeUtc", "ContentHash", "IndexedAtUtc")
            VALUES
                ($id, $path, $rel, $name, 0, '2026-07-01 00:00:00', 'hash', '2026-07-01 00:00:00');
            """;
        _ = command.Parameters.AddWithValue("$id", id.ToString("D").ToUpperInvariant());
        _ = command.Parameters.AddWithValue("$path", absolutePath);
        _ = command.Parameters.AddWithValue("$rel", "MixedCase.md");
        _ = command.Parameters.AddWithValue("$name", "MixedCase");
        _ = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private async Task<Guid?> FindIdByPathAsync(string absolutePath)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """SELECT "Id" FROM "MarkdownFiles" WHERE "AbsolutePath" = $path;""";
        _ = command.Parameters.AddWithValue("$path", absolutePath);
        object? raw = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return raw is string text ? Guid.Parse(text) : null;
    }

    private async Task<bool> TriggerExistsAsync(string triggerName)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name = $name;";
        _ = command.Parameters.AddWithValue("$name", triggerName);
        long count = (long)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;
        return count > 0;
    }
}
