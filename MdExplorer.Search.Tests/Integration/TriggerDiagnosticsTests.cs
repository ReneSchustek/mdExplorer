using MdExplorer.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Search.Tests.Integration;

/// <summary>
/// Stellt sicher, dass der <c>AFTER DELETE</c>-Trigger auf <c>MarkdownDocuments</c> die zugehörige
/// FTS5-Zeile sofort entfernt. Der Test deckt den Fall ab, dass EF Core Guid-Werte als Uppercase-TEXT
/// schreibt (BINARY-Collation case-sensitive) — der Maintainer muss dasselbe Format verwenden.
/// </summary>
public sealed class TriggerDiagnosticsTests
{
    [Fact]
    public async Task Trigger_OnDocumentDelete_RemovesFtsRowImmediately()
    {
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            Guid fileId = Guid.NewGuid();
            await harness.SeedAsync(new SeedDocument(fileId, "Diag", @"C:\Wurzel\diag.md", "diag.md",
                "hash", "{}", "Inhalt diagnostisch.", "Inhalt diagnostisch.", []),
                CancellationToken.None).ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(CancellationToken.None).ConfigureAwait(true);

            using IServiceScope scope = harness.Services.CreateScope();
            MdExplorerDbContext db = scope.ServiceProvider.GetRequiredService<MdExplorerDbContext>();
            SqliteConnection connection = (SqliteConnection)db.Database.GetDbConnection();
            await connection.OpenAsync().ConfigureAwait(true);

            long before = await CountAsync(connection, fileId).ConfigureAwait(true);
            using (SqliteCommand del = connection.CreateCommand())
            {
                del.CommandText = "DELETE FROM MarkdownDocuments WHERE MarkdownFileId = $id";
                _ = del.Parameters.AddWithValue("$id", fileId.ToString("D").ToUpperInvariant());
                _ = await del.ExecuteNonQueryAsync().ConfigureAwait(true);
            }
            long after = await CountAsync(connection, fileId).ConfigureAwait(true);

            Assert.Equal(1L, before);
            Assert.Equal(0L, after);
        }
    }

    private static async Task<long> CountAsync(SqliteConnection connection, Guid fileId)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM MarkdownSearchIndex WHERE MarkdownFileId = $id";
        _ = cmd.Parameters.AddWithValue("$id", fileId.ToString("D").ToUpperInvariant());
        return (long)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))!;
    }
}
