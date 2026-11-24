using System.Data.Common;
using MdExplorer.Core.Models;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Prüft die Persistenz der Fehlschlag-Vermerke gegen echtes SQLite: Upsert, Entfernen,
/// Zählen und das Wegräumen mit der zugehörigen Datei.
/// </summary>
public sealed class ParseFailureRepositoryTests : IAsyncDisposable
{
    private readonly TestIdSequence _ids = new();
    private static readonly Guid FileId = new("11111111-1111-1111-1111-111111111111");

    private readonly SqliteConnection _connection;
    private readonly MdExplorerDbContext _dbContext;
    private readonly ParseFailureRepository _repository;
    private readonly ReaderCounter _readers = new();

    public ParseFailureRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        SqliteParameterLimit.Restrict(_connection);

        DbContextOptions<MdExplorerDbContext> options = new DbContextOptionsBuilder<MdExplorerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new SqlitePragmaInterceptor(), _readers)
            .Options;

        _dbContext = new MdExplorerDbContext(options);
        _ = _dbContext.Database.EnsureCreated();
        _repository = new ParseFailureRepository(_dbContext);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task RecordAsync_OnNewFile_StoresTheMark()
    {
        _ = await SeedFileAsync(FileId, "hash-1").ConfigureAwait(true);

        await _repository.RecordAsync(CreateFailure(FileId, "hash-1"), TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _repository.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        IReadOnlyDictionary<Guid, ParseFailure> stored = await _repository
            .GetByMarkdownFileIdsAsync([FileId], TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Equal("hash-1", stored[FileId].ContentHash);
    }

    [Fact]
    public async Task RecordAsync_OnSecondFailure_OverwritesInsteadOfDuplicating()
    {
        _ = await SeedFileAsync(FileId, "hash-1").ConfigureAwait(true);
        await _repository.RecordAsync(CreateFailure(FileId, "hash-1"), TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _repository.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        await _repository.RecordAsync(CreateFailure(FileId, "hash-2"), TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _repository.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, await _repository.CountAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
        IReadOnlyDictionary<Guid, ParseFailure> stored = await _repository
            .GetByMarkdownFileIdsAsync([FileId], TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Equal("hash-2", stored[FileId].ContentHash);
    }

    /// <remarks>
    /// Der Parser lädt die Vermerke einer Portion vorab. Fragte jedes Vermerken danach noch einmal,
    /// wären es bei 500 gescheiterten Dateien 500 Abfragen statt keiner.
    /// </remarks>
    [Fact]
    public async Task RecordAsync_AfterLoadingTheMarksOfABatch_AsksTheDatabaseNoMore()
    {
        Guid[] ids =
        [
            new("21111111-1111-1111-1111-111111111111"),
            new("31111111-1111-1111-1111-111111111111"),
            new("41111111-1111-1111-1111-111111111111"),
        ];
        foreach (Guid id in ids)
        {
            _ = await SeedFileAsync(id, "hash-1").ConfigureAwait(true);
            await _repository.RecordAsync(CreateFailure(id, "hash-1"), TestContext.Current.CancellationToken).ConfigureAwait(true);
        }
        _ = await _repository.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _repository.GetByMarkdownFileIdsAsync(ids, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _readers.Count = 0;

        foreach (Guid id in ids)
        {
            await _repository.RecordAsync(CreateFailure(id, "hash-2"), TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        Assert.Equal(0, _readers.Count);
    }

    [Fact]
    public async Task RemoveAsync_OnRecoveredFile_DropsTheMark()
    {
        _ = await SeedFileAsync(FileId, "hash-1").ConfigureAwait(true);
        await _repository.RecordAsync(CreateFailure(FileId, "hash-1"), TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _repository.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        await _repository.RemoveAsync([FileId], TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _repository.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, await _repository.CountAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task RemoveAsync_OnEmptyList_DoesNothing()
    {
        await _repository.RemoveAsync([], TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, await _repository.CountAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task GetByMarkdownFileIdsAsync_OnEmptyList_ReturnsEmpty()
    {
        IReadOnlyDictionary<Guid, ParseFailure> result = await _repository
            .GetByMarkdownFileIdsAsync([], TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByMarkdownFileIdsAsync_OnManyIds_DoesNotExceedSqliteParameterLimit()
    {
        // Über der Grenze von 999 gebundenen Parametern, auf die die Verbindung gesetzt ist.
        const int BatchSize = 2_000;
        List<Guid> ids = SqliteParameterLimit.DistinctIds(BatchSize);

        IReadOnlyDictionary<Guid, ParseFailure> result = await _repository
            .GetByMarkdownFileIdsAsync(ids, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DeletingTheFile_AlsoRemovesTheMark()
    {
        MarkdownFile file = await SeedFileAsync(FileId, "hash-1").ConfigureAwait(true);
        await _repository.RecordAsync(CreateFailure(FileId, "hash-1"), TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _repository.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        // Ohne verfolgte Vermerke und über die Mengen-Löschung: So muss der Fremdschlüssel der
        // Datenbank den Vermerk mitnehmen, nicht das Rahmenwerk auf Client-Seite.
        _dbContext.ChangeTracker.Clear();
        _ = await new MarkdownFileRepository(_dbContext).RemoveRangeAsync([file.Id], TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, await _repository.CountAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    private ParseFailure CreateFailure(Guid markdownFileId, string contentHash) =>
        new()
        {
            Id = _ids.Next(),
            MarkdownFileId = markdownFileId,
            ContentHash = contentHash,
            EngineVersion = "test-engine/1",
            FailureReason = "ArgumentException: depth limit exceeded",
            FailedAtUtc = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc),
        };

    private async Task<MarkdownFile> SeedFileAsync(Guid id, string contentHash)
    {
        MarkdownFile file = new()
        {
            Id = id,
            // Je Kennung eine eigene Datei: Der Pfad ist im Index eindeutig.
            AbsolutePath = $@"C:\notes\{id:N}.md",
            RelativePath = $"{id:N}.md",
            FileNameWithoutExtension = id.ToString("N"),
            SizeBytes = 42,
            LastWriteTimeUtc = new DateTime(2026, 8, 18, 11, 0, 0, DateTimeKind.Utc),
            ContentHash = contentHash,
            IndexedAtUtc = new DateTime(2026, 8, 18, 11, 5, 0, DateTimeKind.Utc),
        };
        _ = await _dbContext.Set<MarkdownFile>().AddAsync(file, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        return file;
    }

    /// <summary>Zählt die lesenden Abfragen an die Datenbank.</summary>
    private sealed class ReaderCounter : DbCommandInterceptor
    {
        public int Count { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
