using System.Globalization;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Diagnostics;
using MdExplorer.Core.FileSystem;
using MdExplorer.Data;
using MdExplorer.Data.Options;
using MdExplorer.Graph.DependencyInjection;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.DependencyInjection;
using MdExplorer.Parser.DependencyInjection;
using MdExplorer.Parser.Services;
using MdExplorer.Search.DependencyInjection;
using MdExplorer.Search.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Benchmarks.Stock;

/// <summary>
/// Der Messbestand samt Datenbank, aufgebaut über dieselben Dienste wie in der Anwendung:
/// Migrationen, Indexer, Parser und FTS5-Schreibweg.
/// </summary>
/// <remarks>
/// <para>
/// Gemessen wird gegen eine Datei auf der Platte, nicht gegen eine Datenbank im Speicher. Die
/// Anwendung liest ihre Datenbank von der Platte, mit WAL und Seitencache; eine Speicher-Datenbank
/// wäre schneller und damit eine Zahl für ein Programm, das es nicht gibt.
/// </para>
/// <para>
/// Der Aufbau dauert und wird deshalb im Temp-Ordner zwischengespeichert. Jede Messklasse läuft in
/// einem eigenen Prozess; ohne den Zwischenspeicher baute jede den Bestand neu. Der Ordnername
/// trägt Größe, Startwert und <see cref="LayoutVersion"/>, damit ein geänderter Erzeuger nie einen
/// alten Bestand misst.
/// </para>
/// </remarks>
internal sealed class IndexedStock : IAsyncDisposable
{
    // Hochzählen, sobald StockGenerator anders erzeugt; sonst läge ein veralteter Bestand bereit.
    private const int LayoutVersion = 2;

    private const string ReadyMarker = "bereit.txt";

    /// <summary>Ordner, in dem Notizen und Datenbank zwischengespeichert liegen.</summary>
    public static string CacheDirectory => Path.Combine(
        Path.GetTempPath(),
        "MdExplorer.Benchmarks",
        string.Create(CultureInfo.InvariantCulture, $"bestand-v{LayoutVersion}-{StockGenerator.FileCount}-{StockGenerator.Seed}"));

    private IndexedStock(string rootPath, ServiceProvider services)
    {
        RootPath = rootPath;
        Services = services;
    }

    /// <summary>Ordner mit den Notizen.</summary>
    public string RootPath { get; }

    /// <summary>Dienste der Anwendung, verbunden mit der Datenbank des Bestands.</summary>
    public ServiceProvider Services { get; }

    /// <summary>Liefert den indizierten Bestand; baut ihn beim ersten Mal auf.</summary>
    /// <param name="cancellationToken">Abbruch.</param>
    /// <returns>Der fertige Bestand.</returns>
    public static async Task<IndexedStock> OpenAsync(CancellationToken cancellationToken)
    {
        string notes = EnsureNotes();
        string database = Path.Combine(CacheDirectory, "index.db");
        string marker = Path.Combine(CacheDirectory, ReadyMarker);

        IndexedStock stock = new(notes, BuildServices(notes, database));

        // Auch der zwischengespeicherte Bestand bekommt jede neue Migration. Sonst mäße „messen,
        // ändern, erneut messen“ nach einem neuen Index weiter gegen das alte Schema, und die
        // Verbesserung bliebe unsichtbar.
        await stock.Services.GetRequiredService<IDatabaseMigrator>().MigrateAsync(cancellationToken).ConfigureAwait(false);
        if (!File.Exists(marker))
        {
            _ = await stock.IndexAsync(cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(marker, "vollständig indiziert", cancellationToken).ConfigureAwait(false);
        }

        return stock;
    }

    /// <summary>Baut einen leeren Index für den Bestand in einer eigenen Datenbank auf.</summary>
    /// <remarks>
    /// Synchron, weil BenchmarkDotNet den Aufbau einer Wiederholung nur synchron aufruft. Die
    /// Migrationen laufen über dieselben Migrationsklassen wie in der Anwendung.
    /// </remarks>
    /// <param name="databasePath">Pfad der neuen Datenbank; eine vorhandene Datei wird ersetzt.</param>
    /// <returns>Der Bestand mit leerer, migrierter Datenbank.</returns>
    public static IndexedStock CreateEmpty(string databasePath)
    {
        string notes = EnsureNotes();
        DeleteDatabase(databasePath);
        ServiceProvider services = BuildServices(notes, databasePath);
        using (IServiceScope scope = services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<MdExplorerDbContext>().Database.Migrate();
        }

        return new IndexedStock(notes, services);
    }

    /// <summary>Löscht eine Datenbank samt ihrer WAL- und SHM-Begleitdateien.</summary>
    /// <param name="databasePath">Pfad der Datenbank.</param>
    public static void DeleteDatabase(string databasePath)
    {
        // Der Verbindungspool hält die Datei sonst offen, und Windows verweigert das Löschen.
        SqliteConnection.ClearAllPools();
        foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            File.Delete(databasePath + suffix);
        }
    }

    /// <summary>
    /// Der Hauptweg beim ersten Start: Dateien erfassen, parsen, in den Volltextindex schreiben.
    /// </summary>
    /// <remarks>Die Datenbank ist schon migriert; das Anlegen gehört nicht in die Messung.</remarks>
    /// <param name="cancellationToken">Abbruch.</param>
    /// <returns>Anzahl der Dateien im Index.</returns>
    public async Task<int> IndexAsync(CancellationToken cancellationToken)
    {
        await Services.GetRequiredService<IIndexer>().RunInitialScanAsync(cancellationToken).ConfigureAwait(false);
        await Services.GetRequiredService<ParseOrchestrator>().RunOnceAsync(cancellationToken).ConfigureAwait(false);

        // Ein Abgleich schreibt höchstens eine Portion; der Hintergrunddienst holt den Rest in
        // den folgenden Takten. Hier läuft er, bis nichts mehr aussteht.
        Fts5IndexMaintainer maintainer = Services.GetRequiredService<Fts5IndexMaintainer>();
        int written;
        do
        {
            written = await maintainer.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        }
        while (written > 0);

        AsyncServiceScope scope = Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            int indexed = await scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>().CountAsync(cancellationToken).ConfigureAwait(false);
            if (indexed != StockGenerator.FileCount)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Der Index enthält {indexed} statt {StockGenerator.FileCount} Dateien; eine Messung darüber sagte nichts."));
            }

            return indexed;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await Services.DisposeAsync().ConfigureAwait(false);

    private static string EnsureNotes()
    {
        string notes = Path.Combine(CacheDirectory, "notizen");
        string marker = Path.Combine(CacheDirectory, "notizen-" + ReadyMarker);
        if (File.Exists(marker))
        {
            return notes;
        }

        if (Directory.Exists(notes))
        {
            Directory.Delete(notes, recursive: true);
        }

        _ = Directory.CreateDirectory(notes);
        _ = StockGenerator.Write(notes);
        File.WriteAllText(marker, "vollständig erzeugt");
        return notes;
    }

    // Dieselben Module wie in der Anwendung. Ersetzt sind nur die Einstellungen (feste Wurzel statt
    // der Datei des Nutzers) und die Protokollierung (die Messung misst sonst das Schreiben ins Log).
    private static ServiceProvider BuildServices(string rootPath, string databasePath)
    {
        ServiceCollection services = new();
        _ = services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _ = services.AddSingleton(TimeProvider.System);
        _ = services.AddSingleton<IFileSystem, LocalFileSystem>();
        _ = services.AddSingleton<IParseFailureStatus, ParseFailureStatus>();
        _ = services.AddSingleton<ISettingsService>(new FixedSettingsService(rootPath));
        _ = services.AddOptions<DatabaseOptions>().Configure(options => options.DatabasePath = databasePath);
        _ = services.AddData();
        _ = services.AddIndexer();
        _ = services.AddParser();
        _ = services.AddSearch();
        _ = services.AddGraph();
        return services.BuildServiceProvider(validateScopes: true);
    }
}
