using System.Data.Common;
using MdExplorer.Core.Abstractions;
using MdExplorer.Data;
using MdExplorer.Data.Options;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Die Datenbank entsteht über den echten Dienstaufbau als Datei und trägt danach die Pragmas, auf
/// die sich Indexer und Oberfläche beim gleichzeitigen Zugriff verlassen. Fehlt der Abfangpunkt in
/// <c>AddData</c>, liefe die Datei ohne WAL und ohne Wartepuffer und meldete Sperren sofort als Fehler.
/// </summary>
public sealed class MdExplorerDbContextTests : IDisposable
{
    private readonly string _directory;
    private readonly string _dbPath;
    private readonly ServiceProvider _services;

    public MdExplorerDbContextTests()
    {
        // Fester Ordner je Testklasse, vorher geleert, damit ein abgebrochener Lauf nichts hinterlässt.
        _directory = Path.Combine(Path.GetTempPath(), "MdExplorer.Tests.Db", nameof(MdExplorerDbContextTests));
        DeleteDirectory();
        _ = Directory.CreateDirectory(_directory);
        _dbPath = Path.Combine(_directory, "test.db");

        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddOptions<DatabaseOptions>()
            .Configure(o => o.DatabasePath = _dbPath)
            .ValidateDataAnnotations();
        _ = services.AddData();
        _services = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        SqliteConnection.ClearAllPools();
        DeleteDirectory();
    }

    [Fact]
    public async Task DbContext_AfterMigration_DatabaseFileExists()
    {
        IDatabaseMigrator migrator = _services.GetRequiredService<IDatabaseMigrator>();

        await migrator.MigrateAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(File.Exists(_dbPath));
    }

    [Fact]
    public async Task DbContext_AfterMigration_PragmasAreApplied()
    {
        IDatabaseMigrator migrator = _services.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        using IServiceScope scope = _services.CreateScope();
        MdExplorerDbContext dbContext = scope.ServiceProvider.GetRequiredService<MdExplorerDbContext>();

        PragmaValues values = await ReadPragmasAsync(dbContext).ConfigureAwait(true);

        Assert.Equal("wal", values.JournalMode, ignoreCase: true);
        // SQLite meldet die Stufen als Zahlen: synchronous 1 ist NORMAL, temp_store 2 ist MEMORY.
        Assert.Equal(1L, values.Synchronous);
        Assert.Equal(2L, values.TempStore);
        Assert.Equal(1L, values.ForeignKeys);
    }

    [Fact]
    public async Task DbContext_AfterMigration_PragmaNumbersMatchTheirConstants()
    {
        // Die Zahlen stehen ausgeschrieben in der konstanten Anweisung. Wer nur die Konstante
        // ändert, änderte sonst nichts an der Datenbank.
        IDatabaseMigrator migrator = _services.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        using IServiceScope scope = _services.CreateScope();
        MdExplorerDbContext dbContext = scope.ServiceProvider.GetRequiredService<MdExplorerDbContext>();

        PragmaValues values = await ReadPragmasAsync(dbContext).ConfigureAwait(true);

        Assert.Equal(SqlitePragmaInterceptor.BusyTimeoutMilliseconds, values.BusyTimeout);
        Assert.Equal(SqlitePragmaInterceptor.PageCacheKibibytes, values.CacheSize);
    }

    [Fact]
    public async Task Migrator_OnSecondRun_IsIdempotent()
    {
        IDatabaseMigrator migrator = _services.GetRequiredService<IDatabaseMigrator>();

        await migrator.MigrateAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await migrator.MigrateAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(File.Exists(_dbPath));
    }

    // Alle Werte in einer Anweisung über die Tabellenfunktionen der Pragmas.
    private static async Task<PragmaValues> ReadPragmasAsync(MdExplorerDbContext dbContext)
    {
        await dbContext.Database.OpenConnectionAsync().ConfigureAwait(true);
        using DbCommand command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT (SELECT * FROM pragma_journal_mode), (SELECT * FROM pragma_synchronous),
                   (SELECT * FROM pragma_temp_store), (SELECT * FROM pragma_foreign_keys),
                   (SELECT * FROM pragma_busy_timeout), (SELECT * FROM pragma_cache_size);
            """;
        using DbDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(true);
        Assert.True(await reader.ReadAsync().ConfigureAwait(true));
        return new PragmaValues(
            reader.GetString(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5));
    }

    private void DeleteDirectory()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // Die gelesenen Pragma-Werte; SQLite liefert Stufen und Grenzen als Ganzzahlen.
    private sealed record PragmaValues(
        string JournalMode,
        long Synchronous,
        long TempStore,
        long ForeignKeys,
        long BusyTimeout,
        long CacheSize);
}
