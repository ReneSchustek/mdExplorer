using System.Data.Common;
using MdExplorer.Core.Models;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Jeder Datenbankfehler verlässt die Datenschicht als <see cref="DbException"/>.
/// </summary>
/// <remarks>
/// Die Hintergrunddienste fangen <see cref="DbException"/>, damit eine belegte Datenbank oder ein
/// verletzter Schlüssel nur einen Durchgang kostet. EF Core verpackt Fehler beim Speichern aber in
/// eine <c>DbUpdateException</c> und meldet einen erschöpften Wiederholungsversuch als
/// <c>RetryLimitExceededException</c>; beide erben nicht von <see cref="DbException"/>. Ohne
/// Übersetzung lief jeder Schreibfehler an den Fängen vorbei und beendete den Dienst.
/// </remarks>
public sealed class DatabaseFailureTranslationTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MdExplorerDbContext _dbContext;

    public DatabaseFailureTranslationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _dbContext = CreateContext(maxRetryCount: 0);
        _ = _dbContext.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task SaveChangesAsync_OnADuplicatePath_ThrowsADbException()
    {
        MarkdownFileRepository repository = new(_dbContext);
        await repository.AddAsync(File(new Guid("11111111-1111-1111-1111-111111111111")), TestContext.Current.CancellationToken);
        await repository.AddAsync(File(new Guid("22222222-2222-2222-2222-222222222222")), TestContext.Current.CancellationToken);

        DbException thrown = await Assert.ThrowsAnyAsync<DbException>(
            () => repository.SaveChangesAsync(TestContext.Current.CancellationToken));

        _ = Assert.IsType<DbUpdateException>(thrown.InnerException);
    }

    [Fact]
    public async Task SaveChangesAsync_OnADuplicateTagSlug_ThrowsADbException()
    {
        TagRepository repository = new(_dbContext);
        _ = _dbContext.Set<Tag>().Add(new Tag { Id = new Guid("33333333-3333-3333-3333-333333333333"), Name = "Projekt", Slug = "projekt" });
        _ = _dbContext.Set<Tag>().Add(new Tag { Id = new Guid("44444444-4444-4444-4444-444444444444"), Name = "projekt", Slug = "projekt" });

        _ = await Assert.ThrowsAnyAsync<DbException>(
            () => repository.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheRetriesAreExhausted_ThrowsADbException()
    {
        // SQLITE_BUSY (5) ist wiederholbar. Ohne Wiederholungsbudget meldet EF Core sofort einen
        // erschöpften Versuch, so wie im Betrieb nach sechs vergeblichen Anläufen.
        const int sqliteBusy = 5;
        IExecutionStrategy strategy = _dbContext.Database.CreateExecutionStrategy();

        DbException thrown = await Assert.ThrowsAnyAsync<DbException>(
            () => strategy.ExecuteAsync<int>(_ => throw new SqliteException("database is locked", sqliteBusy), TestContext.Current.CancellationToken));

        Assert.NotNull(thrown.InnerException);
    }

    private static MarkdownFile File(Guid id) => new()
    {
        Id = id,
        AbsolutePath = @"C:\Notizen\doppelt.md",
        RelativePath = "doppelt.md",
        FileNameWithoutExtension = "doppelt",
        ContentHash = "h",
    };

    private MdExplorerDbContext CreateContext(int maxRetryCount)
    {
        DbContextOptions<MdExplorerDbContext> options = new DbContextOptionsBuilder<MdExplorerDbContext>()
            .UseSqlite(_connection, sqlite => sqlite.ExecutionStrategy(
                dependencies => new SqliteRetryingExecutionStrategy(dependencies, maxRetryCount, TimeSpan.Zero)))
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        return new MdExplorerDbContext(options);
    }
}
