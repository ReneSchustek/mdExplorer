using MdExplorer.Core.Abstractions;
using MdExplorer.Data;
using MdExplorer.Data.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Data.Tests;

public sealed class MdExplorerDbContextTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _services;

    public MdExplorerDbContextTests()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "MdExplorer.Tests.Db", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(testRoot);
        _dbPath = Path.Combine(testRoot, "test.db");

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
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
        string? directory = Path.GetDirectoryName(_dbPath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DbContext_AfterMigration_DatabaseFileExists()
    {
        IDatabaseMigrator migrator = _services.GetRequiredService<IDatabaseMigrator>();

        await migrator.MigrateAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.True(File.Exists(_dbPath));
    }

    [Fact]
    public async Task DbContext_AfterMigration_PragmasAreApplied()
    {
        IDatabaseMigrator migrator = _services.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateAsync(CancellationToken.None).ConfigureAwait(true);

        using IServiceScope scope = _services.CreateScope();
        MdExplorerDbContext dbContext = scope.ServiceProvider.GetRequiredService<MdExplorerDbContext>();

        string journalMode = await ReadJournalModeAsync(dbContext).ConfigureAwait(true);
        string synchronous = await ReadSynchronousAsync(dbContext).ConfigureAwait(true);
        string foreignKeys = await ReadForeignKeysAsync(dbContext).ConfigureAwait(true);
        string busyTimeout = await ReadBusyTimeoutAsync(dbContext).ConfigureAwait(true);

        Assert.Equal("wal", journalMode, ignoreCase: true);
        Assert.Equal("1", synchronous);
        Assert.Equal("1", foreignKeys);
        Assert.Equal("5000", busyTimeout);
    }

    [Fact]
    public async Task Migrator_OnSecondRun_IsIdempotent()
    {
        IDatabaseMigrator migrator = _services.GetRequiredService<IDatabaseMigrator>();

        await migrator.MigrateAsync(CancellationToken.None).ConfigureAwait(true);
        await migrator.MigrateAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.True(File.Exists(_dbPath));
    }

    private static async Task<string> ReadJournalModeAsync(MdExplorerDbContext dbContext)
    {
        await dbContext.Database.OpenConnectionAsync().ConfigureAwait(true);
        using System.Data.Common.DbCommand command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        object? value = await command.ExecuteScalarAsync().ConfigureAwait(true);
        return value?.ToString() ?? string.Empty;
    }

    private static async Task<string> ReadSynchronousAsync(MdExplorerDbContext dbContext)
    {
        await dbContext.Database.OpenConnectionAsync().ConfigureAwait(true);
        using System.Data.Common.DbCommand command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA synchronous;";
        object? value = await command.ExecuteScalarAsync().ConfigureAwait(true);
        return value?.ToString() ?? string.Empty;
    }

    private static async Task<string> ReadForeignKeysAsync(MdExplorerDbContext dbContext)
    {
        await dbContext.Database.OpenConnectionAsync().ConfigureAwait(true);
        using System.Data.Common.DbCommand command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";
        object? value = await command.ExecuteScalarAsync().ConfigureAwait(true);
        return value?.ToString() ?? string.Empty;
    }

    private static async Task<string> ReadBusyTimeoutAsync(MdExplorerDbContext dbContext)
    {
        await dbContext.Database.OpenConnectionAsync().ConfigureAwait(true);
        using System.Data.Common.DbCommand command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA busy_timeout;";
        object? value = await command.ExecuteScalarAsync().ConfigureAwait(true);
        return value?.ToString() ?? string.Empty;
    }
}
