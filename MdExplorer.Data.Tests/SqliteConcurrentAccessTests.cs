using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Data;
using MdExplorer.Data.Options;
using MdExplorer.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Regressionstest für die WAL-/busy_timeout-Konfiguration des SQLite-Pragma-Interceptors.
/// Vor dem Fix führte <c>SqliteCacheMode.Shared</c> in Kombination mit fehlendem
/// <c>busy_timeout</c> zu sofortigem <c>SQLITE_LOCKED</c> (Code 6),
/// wenn parallel ein Schreib- und ein Lese-Scope auf denselben Tabellen
/// arbeiteten — exakt der vom User berichtete Klick-auf-Datei-Crash
/// während des Initial-Scans.
/// </summary>
public sealed class SqliteConcurrentAccessTests : IDisposable
{
    private const int WriterIterations = 12;
    private const int FilesPerWriterIteration = 25;
    private const int ReaderIterations = 200;

    private static readonly DateTime FixedUtc = new(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath;
    private readonly ServiceProvider _services;

    public SqliteConcurrentAccessTests()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "MdExplorer.Tests.Concurrency", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(testRoot);
        _dbPath = Path.Combine(testRoot, "concurrent.db");

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
        string? directory = Path.GetDirectoryName(_dbPath);
        if (directory is not null && Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // WAL-Begleitdateien können kurz nach Schließen noch gehalten werden — Temp-Cleanup räumt auf.
            }
        }
    }

    [Fact]
    public async Task ParallelWriterAndReader_OnSharedDatabase_DoesNotThrowSqliteLockedException()
    {
        IDatabaseMigrator migrator = _services.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateAsync(CancellationToken.None).ConfigureAwait(true);

        Guid seedId = await SeedReadTargetAsync().ConfigureAwait(true);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        Task writer = Task.Run(() => SimulateProgressiveScanAsync(cts.Token), cts.Token);
        Task reader = Task.Run(() => HammerRepositoryReadsAsync(seedId, cts.Token), cts.Token);

        await Task.WhenAll(writer, reader).ConfigureAwait(true);
    }

    private async Task<Guid> SeedReadTargetAsync()
    {
        Guid id = Guid.NewGuid();
        using IServiceScope scope = _services.CreateScope();
        IMarkdownFileRepository repository = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
        await repository.AddAsync(BuildFile(id, @"C:\Wurzel\seed.md", "seed-hash"), CancellationToken.None).ConfigureAwait(true);
        _ = await repository.SaveChangesAsync(CancellationToken.None).ConfigureAwait(true);
        return id;
    }

    private async Task SimulateProgressiveScanAsync(CancellationToken cancellationToken)
    {
        // Mimik des Initial-Scans aus MarkdownIndexer: pro Batch ein frischer Scope/DbContext,
        // SaveChangesAsync danach. Genau dieses Muster löste vor dem Fix den Shared-Cache-Lock aus.
        for (int batch = 0; batch < WriterIterations; batch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IServiceScope scope = _services.CreateScope();
            await using (((IAsyncDisposable)scope).ConfigureAwait(false))
            {
                IMarkdownFileRepository repository = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
                for (int item = 0; item < FilesPerWriterIteration; item++)
                {
                    string path = string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $@"C:\Wurzel\batch{batch:D3}\file{item:D3}.md");
                    MarkdownFile entity = BuildFile(Guid.NewGuid(), path, "hash-" + batch + "-" + item);
                    await repository.AddAsync(entity, cancellationToken).ConfigureAwait(false);
                }
                _ = await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HammerRepositoryReadsAsync(Guid seedId, CancellationToken cancellationToken)
    {
        // UI-Pfad simuliert: jeder Lesezugriff öffnet einen eigenen Scope (wie PreviewViewModel/IDocumentLocator).
        for (int iteration = 0; iteration < ReaderIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IServiceScope scope = _services.CreateScope();
            await using (((IAsyncDisposable)scope).ConfigureAwait(false))
            {
                IMarkdownFileRepository repository = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
                MarkdownFile? loaded = await repository
                    .GetByAbsolutePathAsync(@"C:\Wurzel\seed.md", cancellationToken)
                    .ConfigureAwait(false);
                Assert.NotNull(loaded);
                Assert.Equal(seedId, loaded.Id);
            }
        }
    }

    private static MarkdownFile BuildFile(Guid id, string absolutePath, string hash) => new()
    {
        Id = id,
        AbsolutePath = absolutePath,
        RelativePath = Path.GetFileName(absolutePath),
        FileNameWithoutExtension = Path.GetFileNameWithoutExtension(absolutePath),
        SizeBytes = 42,
        LastWriteTimeUtc = FixedUtc,
        ContentHash = hash,
        IndexedAtUtc = FixedUtc,
    };
}
