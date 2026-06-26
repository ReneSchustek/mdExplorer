using MdExplorer.Core.Abstractions;
using MdExplorer.Data.Migrations;
using MdExplorer.Data.Options;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MdExplorer.Data;

/// <summary>
/// DI-Registrierung der Data-Schicht.
/// </summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registriert <see cref="MdExplorerDbContext"/> mit SQLite, Pragma-Interceptor und Migrator.
    /// Erwartet, dass <see cref="DatabaseOptions"/> bereits via <c>AddOptions</c> registriert wurde.
    /// </summary>
    public static IServiceCollection AddData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddSingleton<SqlitePragmaInterceptor>();

        _ = services.AddDbContext<MdExplorerDbContext>((serviceProvider, optionsBuilder) =>
        {
            DatabaseOptions databaseOptions = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            SqlitePragmaInterceptor pragmaInterceptor = serviceProvider.GetRequiredService<SqlitePragmaInterceptor>();

            // Cache-Modus bewusst auf Default/Private — Shared-Cache erzeugt
            // SQLITE_LOCKED_SHAREDCACHE (Extended-Code 262) sobald zwei Scopes
            // parallel auf dieselben Tabellen zugreifen. WAL plus busy_timeout
            // (im Pragma-Interceptor) löst Reader/Writer-Parallelität sauber.
            SqliteConnectionStringBuilder connectionStringBuilder = new()
            {
                DataSource = databaseOptions.DatabasePath,
                Pooling = true,
            };
            _ = optionsBuilder
                .UseSqlite(
                    connectionStringBuilder.ToString(),
                    sqlite =>
                    {
                        _ = sqlite.CommandTimeout(databaseOptions.CommandTimeoutSeconds);
                        _ = sqlite.ExecutionStrategy(
                            deps => new SqliteRetryingExecutionStrategy(deps));
                    })
                .AddInterceptors(pragmaInterceptor);
        });

        _ = services.AddSingleton<IDatabaseMigrator, DatabaseMigrator>();
        _ = services.AddScoped<IMarkdownFileRepository, MarkdownFileRepository>();
        _ = services.AddScoped<IMarkdownDocumentRepository, MarkdownDocumentRepository>();
        _ = services.AddScoped<IMarkdownSourceProvider, MarkdownSourceProvider>();
        _ = services.AddScoped<ITagRepository, TagRepository>();
        _ = services.AddScoped<IGraphSourceProvider, GraphSourceProvider>();
        _ = services.AddScoped<ISearchSourceProvider, SearchSourceProvider>();
        _ = services.AddScoped<ISearchIndexStorage, SqliteSearchIndexStorage>();
        _ = services.AddScoped<ITagStatisticsQuery, TagStatisticsQuery>();
        _ = services.AddScoped<ITagFileLookupQuery, TagFileLookupQuery>();
        _ = services.AddScoped<IAllFilesQuery, AllFilesQuery>();

        return services;
    }
}
