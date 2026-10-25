using System.Data.Common;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Die Volltext-Befehle laufen über eine Verbindung, die der Kontext öffnet. Öffnete der Storage
/// sie selbst, liefe der Pragma-Abfangpunkt nicht: kein Wartepuffer, keine Fremdschlüssel, und
/// der eingestellte Befehls-Timeout gälte für den schwersten Schreibpfad nicht.
/// </summary>
public sealed class SqliteSearchIndexStorageConnectionTests : IDisposable
{
    private const int ConfiguredTimeoutSeconds = 7;

    private readonly string _directory;
    private readonly string _databasePath;

    public SqliteSearchIndexStorageConnectionTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "MdExplorer.Tests.Db", nameof(SqliteSearchIndexStorageConnectionTests));
        DeleteDirectory();
        _ = Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "index.db");

        using MdExplorerDbContext setup = CreateContext(new CountingInterceptor());
        setup.Database.Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        DeleteDirectory();
    }

    [Fact]
    public async Task LoadIndexedHashesAsync_OpensTheConnectionThroughTheContext()
    {
        CountingInterceptor interceptor = new();
        await using MdExplorerDbContext dbContext = CreateContext(interceptor);
        SqliteSearchIndexStorage sut = new(dbContext);

        _ = await sut.LoadIndexedHashesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(interceptor.OpenedCount > 0);
    }

    [Fact]
    public async Task ApplyChangesAsync_UsesTheCommandTimeoutOfTheContext()
    {
        await using MdExplorerDbContext dbContext = CreateContext(new CountingInterceptor());
        SqliteSearchIndexStorage sut = new(dbContext);

        await sut.ApplyChangesAsync(
            [Guid.Parse("11111111-1111-1111-1111-111111111111")],
            [],
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(ConfiguredTimeoutSeconds, ((SqliteConnection)dbContext.Database.GetDbConnection()).DefaultTimeout);
    }

    private MdExplorerDbContext CreateContext(CountingInterceptor interceptor)
    {
        DbContextOptions<MdExplorerDbContext> options = new DbContextOptionsBuilder<MdExplorerDbContext>()
            .UseSqlite($"Data Source={_databasePath}", sqlite => sqlite.CommandTimeout(ConfiguredTimeoutSeconds))
            .AddInterceptors(new SqlitePragmaInterceptor(), interceptor)
            .Options;
        return new MdExplorerDbContext(options);
    }

    private void DeleteDirectory()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // Zählt, wie oft der Kontext eine Verbindung geöffnet hat.
    private sealed class CountingInterceptor : DbConnectionInterceptor
    {
        public int OpenedCount { get; private set; }

        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            OpenedCount++;
            return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
        }
    }
}
