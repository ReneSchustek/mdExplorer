using System.IO;
using MdExplorer.Core.Models;
using MdExplorer.Data;
using MdExplorer.Data.Repositories;
using MdExplorer.TagCloud.Services;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.TagCloud.Tests.Services;

/// <summary>
/// Pflanzt eine echte SQLite-Datei für die Tag-Statistik-Tests und liefert einen Service,
/// der gegen sie arbeitet. SQLite-Datei statt In-Memory, damit Performance-Messungen
/// das tatsächliche Page-Cache-Verhalten zeigen. Die Query-Implementierung kommt aus der
/// Data-Schicht (<see cref="TagStatisticsQuery"/>), der TagCloud-Service mappt das Aggregat
/// auf das TagCloud-Domain-Modell.
/// </summary>
internal sealed class TagStatisticsTestHarness : IAsyncDisposable
{
    private readonly string _databasePath;
    private readonly DbContextOptions<MdExplorerDbContext> _contextOptions;
    private readonly MdExplorerDbContext _serviceContext;

    public TagStatisticsTestHarness()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mdex-tagcloud-{Guid.NewGuid():N}.db");
        DbContextOptionsBuilder<MdExplorerDbContext> builder = new();
        _ = builder.UseSqlite($"Data Source={_databasePath}");
        _contextOptions = builder.Options;

        using (MdExplorerDbContext setupContext = new(_contextOptions))
        {
            setupContext.Database.Migrate();
        }
        _serviceContext = new MdExplorerDbContext(_contextOptions);
        Service = new TagStatisticsService(new TagStatisticsQuery(_serviceContext));
    }

    public TagStatisticsService Service { get; }

    public async Task SeedAsync(IReadOnlyList<SeedTag> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        MdExplorerDbContext context = new(_contextOptions);
        await using (context.ConfigureAwait(true))
        {
            foreach (SeedTag tag in tags)
            {
                Tag persistedTag = new()
                {
                    Id = Guid.NewGuid(),
                    Name = tag.Name,
                    Slug = tag.Slug,
                };
                _ = context.Set<Tag>().Add(persistedTag);

                foreach (DateTime fileTime in tag.FileLastWriteTimesUtc)
                {
                    MarkdownFile file = CreateMarkdownFile(fileTime);
                    _ = context.Set<MarkdownFile>().Add(file);
                    _ = context.Set<MarkdownFileTag>().Add(new MarkdownFileTag
                    {
                        MarkdownFileId = file.Id,
                        TagId = persistedTag.Id,
                    });
                }
            }
            _ = await context.SaveChangesAsync().ConfigureAwait(true);
        }
    }

    public async Task SeedLargeDatasetAsync(int documentCount, int distinctTagCount, DateTime baseTime)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(documentCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(distinctTagCount);
        MdExplorerDbContext context = new(_contextOptions);
        await using (context.ConfigureAwait(true))
        {
            _ = await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;").ConfigureAwait(true);

            Tag[] tags = new Tag[distinctTagCount];
            for (int tagIndex = 0; tagIndex < distinctTagCount; tagIndex++)
            {
                tags[tagIndex] = new Tag
                {
                    Id = Guid.NewGuid(),
                    Name = $"Tag{tagIndex:D4}",
                    Slug = $"tag-{tagIndex:D4}",
                };
            }
            context.Set<Tag>().AddRange(tags);

            for (int documentIndex = 0; documentIndex < documentCount; documentIndex++)
            {
                MarkdownFile file = CreateMarkdownFile(baseTime.AddMinutes(documentIndex));
                _ = context.Set<MarkdownFile>().Add(file);

                int tagsPerDoc = 1 + (documentIndex % 5);
                for (int linkIndex = 0; linkIndex < tagsPerDoc; linkIndex++)
                {
                    Tag tag = tags[(documentIndex + linkIndex) % distinctTagCount];
                    _ = context.Set<MarkdownFileTag>().Add(new MarkdownFileTag
                    {
                        MarkdownFileId = file.Id,
                        TagId = tag.Id,
                    });
                }
            }
            _ = await context.SaveChangesAsync().ConfigureAwait(true);
        }
    }

    private static MarkdownFile CreateMarkdownFile(DateTime lastWriteTimeUtc) => new()
    {
        Id = Guid.NewGuid(),
        AbsolutePath = $"C:\\notes\\{Guid.NewGuid():N}.md",
        RelativePath = $"notes/{Guid.NewGuid():N}.md",
        FileNameWithoutExtension = Guid.NewGuid().ToString("N"),
        SizeBytes = 1024,
        LastWriteTimeUtc = lastWriteTimeUtc,
        ContentHash = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()),
        IndexedAtUtc = lastWriteTimeUtc,
    };

    public async ValueTask DisposeAsync()
    {
        await _serviceContext.DisposeAsync().ConfigureAwait(true);
        try
        {
            File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // Temp-Datei wird beim nächsten OS-Cleanup entfernt.
        }
    }
}

internal sealed record SeedTag(string Name, string Slug, params DateTime[] FileLastWriteTimesUtc);
