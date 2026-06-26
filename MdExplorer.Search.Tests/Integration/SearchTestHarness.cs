using System.IO.Compression;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Data;
using MdExplorer.Data.Repositories;
using MdExplorer.Search.Options;
using MdExplorer.Search.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Search.Tests.Integration;

/// <summary>
/// Test-Harness für die FTS5-Integrationstests. Legt eine echte SQLite-Datei (kein In-Memory) an,
/// wendet die EF-Migrationen auf den DbContext an, instanziiert die produktive
/// <see cref="Fts5SearchService"/>- und <see cref="Fts5IndexMaintainer"/>-Pipeline samt
/// den Data-Implementierungen der Core-Abstraktionen.
/// </summary>
internal sealed class SearchTestHarness : IAsyncDisposable
{
    private static readonly DateTime FixedUtc = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly string _databasePath;
    private readonly ServiceProvider _serviceProvider;

    public SearchTestHarness()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mdex-search-{Guid.NewGuid():N}.db");
        FileSystem = new FakeFileSystem();
        TimeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero));

        ServiceCollection services = [];
        _ = services.AddLogging();
        _ = services.AddDbContext<MdExplorerDbContext>(options =>
        {
            _ = options.UseSqlite($"Data Source={_databasePath}");
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
            NullLogger<Fts5IndexMaintainer>.Instance));

        _serviceProvider = services.BuildServiceProvider();

        using IServiceScope scope = _serviceProvider.CreateScope();
        MdExplorerDbContext dbContext = scope.ServiceProvider.GetRequiredService<MdExplorerDbContext>();
        dbContext.Database.Migrate();
    }

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
                Id = Guid.NewGuid(),
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
                    Id = Guid.NewGuid(),
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
        if (File.Exists(_databasePath))
        {
            try
            {
                File.Delete(_databasePath);
            }
            catch (IOException)
            {
                // Datei wird bei der nächsten Iteration vom Temp-Cleanup entfernt.
            }
        }
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
