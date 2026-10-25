using MdExplorer.Core.Abstractions;
using MdExplorer.Data.Migrations;
using MdExplorer.Data.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Vor einer Migration liegt eine Kopie der bestehenden Datenbank neben ihr. Eine Migration baut
/// Tabellen neu auf und löscht im Zweifel Zeilen; ohne Kopie wäre der Stand davor verloren.
/// </summary>
public sealed class DatabaseMigratorBackupTests : IDisposable
{
    // Ein Stand mitten in der Kette, damit danach noch Migrationen ausstehen.
    private const string EarlierMigration = "20260609172327_RestoreFtsCleanupTriggersAfterRebuild";

    private readonly string _directory;
    private readonly string _databasePath;
    private readonly ServiceProvider _services;

    public DatabaseMigratorBackupTests()
    {
        // Fester Ordner je Testklasse statt einer Zufallskennung; vorher geleert, damit ein
        // abgebrochener Lauf nichts hinterlässt, das den nächsten verfälscht.
        _directory = Path.Combine(Path.GetTempPath(), "MdExplorer.Tests.Db", nameof(DatabaseMigratorBackupTests));
        DeleteDirectory();
        _ = Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "index.db");

        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddOptions<DatabaseOptions>().Configure(o => o.DatabasePath = _databasePath);
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
    public async Task MigrateAsync_WithPendingMigrationsOnAnExistingDatabase_WritesABackupOfTheOldState()
    {
        await MigrateToAsync(EarlierMigration).ConfigureAwait(true);
        await InsertFileAsync().ConfigureAwait(true);

        await _services.GetRequiredService<IDatabaseMigrator>().MigrateAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        string backupPath = _databasePath + DatabaseMigrator.BackupSuffix;
        Assert.True(File.Exists(backupPath));
        Assert.Equal(1L, await CountFilesAsync(backupPath).ConfigureAwait(true));
    }

    [Fact]
    public async Task MigrateAsync_OnANewDatabase_WritesNoBackup()
    {
        await _services.GetRequiredService<IDatabaseMigrator>().MigrateAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(File.Exists(_databasePath + DatabaseMigrator.BackupSuffix));
    }

    private async Task MigrateToAsync(string migration)
    {
        using IServiceScope scope = _services.CreateScope();
        MdExplorerDbContext dbContext = scope.ServiceProvider.GetRequiredService<MdExplorerDbContext>();
        IMigrator migrator = dbContext.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync(migration, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private async Task InsertFileAsync()
    {
        using IServiceScope scope = _services.CreateScope();
        MdExplorerDbContext dbContext = scope.ServiceProvider.GetRequiredService<MdExplorerDbContext>();
        _ = await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "MarkdownFiles"
                ("Id", "AbsolutePath", "RelativePath", "FileNameWithoutExtension",
                 "SizeBytes", "LastWriteTimeUtc", "ContentHash", "IndexedAtUtc")
            VALUES
                ('EEEEEEEE-1111-2222-3333-444444444444', 'C:\Notes\a.md', 'a.md', 'a', 0,
                 '2026-07-01 00:00:00', 'hash', '2026-07-01 00:00:00');
            """,
            TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static async Task<long> CountFilesAsync(string databasePath)
    {
        using SqliteConnection connection = new($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM "MarkdownFiles";""";
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken).ConfigureAwait(true) ?? 0L);
    }

    private void DeleteDirectory()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
