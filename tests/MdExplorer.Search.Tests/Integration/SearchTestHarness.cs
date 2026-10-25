using System.IO.Compression;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Data;
using MdExplorer.Data.Repositories;
using MdExplorer.Search.Options;
using MdExplorer.Search.Services;
using MdExplorer.Search.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Search.Tests.Integration;

/// <summary>
/// Prüfstand für die FTS5-Integrationstests: eine echte SQLite-Datei mit allen Migrationen, darüber
/// der produktive <see cref="Fts5SearchService"/> und <see cref="Fts5IndexMaintainer"/> samt den
/// Repositories der Datenschicht. Nur Dateisystem und Uhr sind Attrappen.
/// </summary>
internal sealed class SearchTestHarness : IAsyncDisposable
{
    private static readonly DateTime FixedUtc = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly string _databasePath;
    private int _nextId;
    private readonly ServiceProvider _serviceProvider;

    // Fortlaufende Kennungen für Dokumente und Schlagworte; die Dateien bringt der Test selbst mit.
    private Guid NextId() => new(Interlocked.Increment(ref _nextId), 0, 0, new byte[8]);

    public SearchTestHarness()
    {
        // Die Datenbank liegt als Datei im Temp-Ordner, und parallel laufende Tests brauchen je eine
        // eigene. Die Zufalls-GUID steht nur im Dateinamen, nie in den Testdaten.
        _databasePath = Path.Combine(Path.GetTempPath(), $"mdex-search-{Guid.NewGuid():N}.db");
        FileSystem = new FakeFileSystem();
        TimeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero));

        ServiceCollection services = [];
        _ = services.AddLogging();
        _ = services.AddDbContext<MdExplorerDbContext>(options =>
        {
            // Ohne Pool schließt jede Verbindung ihre Datei beim Entsorgen. Mit Pool hielte eine
            // ruhende Verbindung die Datenbank offen, und das Aufräumen am Testende scheiterte.
            _ = options.UseSqlite($"Data Source={_databasePath};Pooling=False");
        });
        _ = services.AddSingleton<IFileSystem>(FileSystem);
        _ = services.AddSingleton<TimeProvider>(TimeProvider);
        _ = services.AddSingleton<SearchOptions>(_ => new SearchOptions());
        _ = services.AddSingleton<IOptions<SearchOptions>>(sp =>
            Microsoft.Extensions.Options.Options.Create(sp.GetRequiredService<SearchOptions>()));
        _ = services.AddScoped<ISearchSourceProvider, SearchSourceProvider>();
        _ = services.AddScoped<ISearchIndexStorage, SqliteSearchIndexStorage>();
        _ = services.AddSingleton<Abstractions.ISynonymProvider, FileSynonymProvider>();
        _ = services.AddSingleton<Abstractions.ISearchQueryBuilder, SearchQueryBuilder>();
        _ = services.AddSingleton<Abstractions.ISimilarityQueryBuilder, SimilarityQueryBuilder>();
        _ = services.AddScoped<Abstractions.ISearchService, Fts5SearchService>();
        _ = services.AddSingleton(sp => new Fts5IndexMaintainer(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<IOptions<SearchOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            new MdExplorer.Core.Diagnostics.BackgroundServiceStatus(),
            NullLogger<Fts5IndexMaintainer>.Instance));

        _serviceProvider = services.BuildServiceProvider();

        using IServiceScope scope = _serviceProvider.CreateScope();
        MdExplorerDbContext dbContext = scope.ServiceProvider.GetRequiredService<MdExplorerDbContext>();
        dbContext.Database.Migrate();
    }

    public string DatabasePath => _databasePath;

    public FakeFileSystem FileSystem { get; }

    public FakeTimeProvider TimeProvider { get; }

    public IServiceProvider Services => _serviceProvider;

    public Fts5IndexMaintainer Maintainer => _serviceProvider.GetRequiredService<Fts5IndexMaintainer>();

    public Abstractions.ISearchService CreateSearchService(IServiceScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.ServiceProvider.GetRequiredService<Abstractions.ISearchService>();
    }

    public async Task SeedAsync(SeedDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        IServiceScope scope = _serviceProvider.CreateScope();
        await using (((IAsyncDisposable)scope).ConfigureAwait(false))
        {
            MdExplorerDbContext dbContext = scope.ServiceProvider.GetRequiredService<MdExplorerDbContext>();

            MarkdownFile file = new()
            {
                Id = document.MarkdownFileId,
                AbsolutePath = document.AbsolutePath,
                RelativePath = document.RelativePath,
                FileNameWithoutExtension = document.Title,
                SizeBytes = 0,
                LastWriteTimeUtc = FixedUtc,
                ContentHash = document.SourceContentHash,
                IndexedAtUtc = FixedUtc,
            };
            _ = await dbContext.Set<MarkdownFile>().AddAsync(file, cancellationToken).ConfigureAwait(false);

            MarkdownDocument doc = new()
            {
                Id = NextId(),
                MarkdownFileId = document.MarkdownFileId,
                SourceContentHash = document.SourceContentHash,
                FrontmatterJson = document.FrontmatterJson,
                OutlinksJson = "[]",
                ParsedAtUtc = FixedUtc,
            };
            doc.SetRenderedHtmlGz(Compress(document.Body));
            _ = await dbContext.Set<MarkdownDocument>().AddAsync(doc, cancellationToken).ConfigureAwait(false);

            foreach ((string tagSlug, string tagName) in document.Tags)
            {
                Tag tag = new()
                {
                    Id = NextId(),
                    Name = tagName,
                    Slug = tagSlug,
                };
                _ = await dbContext.Set<Tag>().AddAsync(tag, cancellationToken).ConfigureAwait(false);

                MarkdownFileTag link = new()
                {
                    MarkdownFileId = document.MarkdownFileId,
                    TagId = tag.Id,
                };
                _ = await dbContext.Set<MarkdownFileTag>().AddAsync(link, cancellationToken).ConfigureAwait(false);
            }

            _ = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            FileSystem.AddFile(document.AbsolutePath, document.RawSource);
        }
    }

    public async Task<int> DeleteDocumentAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        IServiceScope scope = _serviceProvider.CreateScope();
        await using (((IAsyncDisposable)scope).ConfigureAwait(false))
        {
            MdExplorerDbContext dbContext = scope.ServiceProvider.GetRequiredService<MdExplorerDbContext>();
            MarkdownDocument? doc = await dbContext.Set<MarkdownDocument>()
                .FirstOrDefaultAsync(d => d.MarkdownFileId == markdownFileId, cancellationToken)
                .ConfigureAwait(false);
            if (doc is null)
            {
                return 0;
            }
            _ = dbContext.Set<MarkdownDocument>().Remove(doc);
            return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync().ConfigureAwait(false);

        // Im WAL-Modus gehören zur Datenbank zwei Begleitdateien; sie gehen mit.
        File.Delete(_databasePath);
        File.Delete(_databasePath + "-wal");
        File.Delete(_databasePath + "-shm");
    }

    private static byte[] Compress(string source)
    {
        using MemoryStream buffer = new();
        using (GZipStream gzip = new(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(source);
            gzip.Write(bytes, 0, bytes.Length);
        }
        return buffer.ToArray();
    }
}

/// <summary>Eine Datei samt geparstem Dokument und Schlagworten, wie sie der Prüfstand anlegt.</summary>
internal sealed record SeedDocument(
    Guid MarkdownFileId,
    string Title,
    string AbsolutePath,
    string RelativePath,
    string SourceContentHash,
    string FrontmatterJson,
    string Body,
    string RawSource,
    IReadOnlyList<(string Slug, string Name)> Tags);
