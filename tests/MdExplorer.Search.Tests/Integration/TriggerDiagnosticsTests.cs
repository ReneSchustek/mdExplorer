using MdExplorer.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Search.Tests.Integration;

/// <summary>
/// Hält fest, wie eine gelöschte Datei aus dem Volltext verschwindet, und wann.
/// </summary>
/// <remarks>
/// <para>
/// <c>AFTER DELETE</c>-Auslöser, sofort und je Zeile, wären zu teuer: <c>MarkdownFileId</c>
/// ist in der FTS5-Tabelle <c>UNINDEXED</c>, jede Bedingung darauf liest die ganze Tabelle.
/// Gemessen rund 170 ms je gelöschter Datei, für 25.000 Einträge über eine Stunde.
/// </para>
/// <para>
/// Deshalb räumt der Abgleich auf, und der läuft alle fünf Sekunden. Diese Prüfungen
/// beschreiben beide Hälften des Handels: <b>unmittelbar nach dem Löschen steht die Zeile
/// noch</b>, nach dem Abgleich ist sie weg. Wer das Fenster für einen Fehler hält, liest hier
/// nach, dass es gewollt ist; dieselbe Frist gilt fürs Hinzufügen ohnehin.
/// </para>
/// <para>
/// Ebenso hält die Datei fest: EF Core schreibt Guid-Werte als
/// Uppercase-<c>D</c>-TEXT, die Collation ist BINARY. Wer im Volltext ein anderes Format
/// verwendet, findet seine eigene Zeile nicht wieder.
/// </para>
/// </remarks>
public sealed class TriggerDiagnosticsTests
{
    private const string DeleteDocumentSql = "DELETE FROM MarkdownDocuments WHERE MarkdownFileId = $id";

    private const string DeleteFileSql = "DELETE FROM MarkdownFiles WHERE Id = $id";

    [Fact]
    public async Task Maintainer_OnDocumentDelete_RemovesFtsRowOnNextRun()
    {
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            Guid fileId = IdOf(1);
            await harness.SeedAsync(new SeedDocument(fileId, "Diag", @"C:\Wurzel\diag.md", "diag.md",
                "hash", "{}", "Inhalt diagnostisch.", "Inhalt diagnostisch.", []),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            MdExplorerDbContext db = scope.ServiceProvider.GetRequiredService<MdExplorerDbContext>();
            SqliteConnection connection = (SqliteConnection)db.Database.GetDbConnection();
            await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            long before = await CountAsync(connection, fileId).ConfigureAwait(true);
            await DeleteAsync(connection, deleteTheFile: false, fileId).ConfigureAwait(true);
            long betweenRuns = await CountAsync(connection, fileId).ConfigureAwait(true);

            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            long after = await CountAsync(connection, fileId).ConfigureAwait(true);

            Assert.Equal(1L, before);
            Assert.Equal(1L, betweenRuns);
            Assert.Equal(0L, after);
        }
    }

    [Fact]
    public async Task Maintainer_OnFileDelete_RemovesFtsRowOnNextRun()
    {
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            Guid fileId = IdOf(2);
            await harness.SeedAsync(new SeedDocument(fileId, "DiagFile", @"C:\Wurzel\diagfile.md", "diagfile.md",
                "hash", "{}", "Inhalt datei-abgleich.", "Inhalt datei-abgleich.", []),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            MdExplorerDbContext db = scope.ServiceProvider.GetRequiredService<MdExplorerDbContext>();
            SqliteConnection connection = (SqliteConnection)db.Database.GetDbConnection();
            await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            long before = await CountAsync(connection, fileId).ConfigureAwait(true);
            await DeleteAsync(connection, deleteTheFile: true, fileId).ConfigureAwait(true);

            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            long after = await CountAsync(connection, fileId).ConfigureAwait(true);

            Assert.Equal(1L, before);
            Assert.Equal(0L, after);
        }
    }

    [Fact]
    public async Task Database_CarriesNoFtsCleanupTriggers()
    {
        // Der Gegenbeleg zu den beiden Prüfungen oben: Was dort der Abgleich tut, tut kein
        // Auslöser. Käme einer hinzu, kostete jedes Löschen die oben gemessenen 170 ms.
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            using IServiceScope scope = harness.Services.CreateScope();
            MdExplorerDbContext db = scope.ServiceProvider.GetRequiredService<MdExplorerDbContext>();
            SqliteConnection connection = (SqliteConnection)db.Database.GetDbConnection();
            await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name LIKE '%FtsCleanup';";
            long triggers = (long)(await cmd
                .ExecuteScalarAsync(TestContext.Current.CancellationToken)
                .ConfigureAwait(true))!;

            Assert.Equal(0L, triggers);
        }
    }

    private static async Task DeleteAsync(SqliteConnection connection, bool deleteTheFile, Guid fileId)
    {
        using SqliteCommand del = connection.CreateCommand();

        // Zwei Konstanten über if/else statt eines übergebenen Texts: So sieht auch die Analyse,
        // dass kein zusammengesetztes SQL ausgeführt wird. Die Kennung reist als Parameter.
        if (deleteTheFile)
        {
            del.CommandText = DeleteFileSql;
        }
        else
        {
            del.CommandText = DeleteDocumentSql;
        }
        _ = del.Parameters.AddWithValue("$id", fileId.ToString("D").ToUpperInvariant());
        _ = await del.ExecuteNonQueryAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    private static async Task<long> CountAsync(SqliteConnection connection, Guid fileId)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM MarkdownSearchIndex WHERE MarkdownFileId = $id";
        _ = cmd.Parameters.AddWithValue("$id", fileId.ToString("D").ToUpperInvariant());
        return (long)(await cmd
            .ExecuteScalarAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(false))!;
    }

    private static Guid IdOf(int index) =>
        new(index.ToString("D8", System.Globalization.CultureInfo.InvariantCulture) + "-0000-0000-0000-000000000000");
}
