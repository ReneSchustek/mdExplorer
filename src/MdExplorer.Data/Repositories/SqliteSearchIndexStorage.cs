using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using MdExplorer.Core.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MdExplorer.Data.Repositories;

/// <summary>
/// SQLite/FTS5-Implementierung von <see cref="ISearchIndexStorage"/>. Kapselt das gesamte
/// SQL der <c>MarkdownSearchIndex</c>-Tabelle, damit das Search-Modul weder EF Core noch
/// <see cref="SqliteConnection"/> direkt kennt.
/// </summary>
/// <remarks>
/// Die Verbindung kommt aus dem <see cref="MdExplorerDbContext"/> und wird über ihn geöffnet. Nur so
/// laufen der Pragma-Abfangpunkt (Wartepuffer, Fremdschlüssel) und die Wiederholungsstrategie auch
/// für die Volltext-Befehle. Den Befehls-Timeout legt EF Core als <c>DefaultTimeout</c> an der
/// Verbindung ab; jeder hier angelegte Befehl übernimmt ihn. Alle Anweisungen sind Konstanten;
/// Listen von Kennungen gehen als ein JSON-Parameter über <c>json_each</c> hinein.
/// </remarks>
public sealed class SqliteSearchIndexStorage : ISearchIndexStorage
{
    // Beide vollständigen Such-Statements werden vom Compiler aus den Konstanten weiter unten
    // zusammengesetzt. Diese eine ist intern statt privat, damit ein Wächter-Test den Abfrageplan
    // der Suche prüfen kann.
    internal const string SelectWithoutPathFilterSql = SelectSearchPrefix + SelectSearchSuffix;

    // "MarkdownFileId" ist in der FTS5-Tabelle UNINDEXED; jede Bedingung darauf liest die ganze
    // Tabelle. Die Kennungen kommen deshalb in einem Zug: eine Anweisung, ein Durchlauf, egal wie
    // viele Kennungen. Gemessen am Messbestand (7.700 Notizen): alle Bodies in einem Durchlauf
    // 104 ms, als IN-Listen zu je 500 wären es 16 Durchläufe zu rund 40 ms.
    internal const string SelectBodiesByIdsSql = """
        SELECT "MarkdownFileId", "Body"
        FROM "MarkdownSearchIndex"
        WHERE "MarkdownFileId" IN (SELECT value FROM json_each($ids));
        """;

    private const string SelectStateSql = """
        SELECT "MarkdownFileId", "SourceContentHash"
        FROM "MarkdownSearchIndex";
        """;

    private const string DeleteByIdsSql = """
        DELETE FROM "MarkdownSearchIndex"
        WHERE "MarkdownFileId" IN (SELECT value FROM json_each($ids));
        """;

    private const string InsertSql = """
        INSERT INTO "MarkdownSearchIndex"
            ("Title", "Body", "Tags", "Frontmatter", "Path", "MarkdownFileId", "SourceContentHash")
        VALUES
            ($title, $body, $tags, $frontmatter, $path, $fileId, $hash);
        """;

    // Gemeinsamer SELECT-Rumpf für die FTS5-Suche. Der WHERE-Block wird je nach
    // Path-Filter unterschiedlich kombiniert; alle Werte bleiben parametrisiert. Die Trefferstellen
    // fasst der Ausschnitt mit den Zeichen aus SnippetMarkers ein (U+E000, U+E001).
    private const string SelectSearchPrefix =
        "SELECT \"MarkdownFileId\", \"Path\", \"Title\", " +
        "snippet(\"MarkdownSearchIndex\", 1, char(57344), char(57345), '…', $snippetTokens) AS \"Snippet\", " +
        "bm25(\"MarkdownSearchIndex\", $wTitle, $wBody, $wTags, $wFrontmatter) AS \"Score\" " +
        "FROM \"MarkdownSearchIndex\" " +
        "WHERE \"MarkdownSearchIndex\" MATCH $match";

    // Der Pfadfilter maskiert % und _ mit Backslash (PathFilterPattern im Such-Modul).
    private const string SelectSearchPathFilter = """ AND "Path" LIKE $path ESCAPE '\'""";

    private const string SelectSearchSuffix = " ORDER BY \"Score\" ASC LIMIT $take OFFSET $skip;";

    // Wie SelectWithoutPathFilterSql vom Compiler aus Konstanten zusammengesetzt.
    private const string SelectWithPathFilterSql = SelectSearchPrefix + SelectSearchPathFilter + SelectSearchSuffix;

    private readonly MdExplorerDbContext _dbContext;

    /// <summary>Erzeugt den Storage und injiziert den DbContext.</summary>
    public SqliteSearchIndexStorage(MdExplorerDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<Guid, string>> LoadIndexedHashesAsync(CancellationToken cancellationToken) =>
        WithOpenConnectionAsync(
            (connection, token) =>
            {
                SqliteCommand command = connection.CreateCommand();
                command.CommandText = SelectStateSql;
                return SearchIndexRows.ReadIdToTextAsync(command, token);
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task ApplyChangesAsync(
        IReadOnlyCollection<Guid> deletes,
        IReadOnlyCollection<SearchIndexEntry> upserts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deletes);
        ArgumentNullException.ThrowIfNull(upserts);
        if (deletes.Count == 0 && upserts.Count == 0)
        {
            return;
        }

        _ = await WithOpenConnectionAsync(
            (connection, token) => ApplyInTransactionAsync(connection, deletes, upserts, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchIndexHit>> QueryAsync(SearchIndexQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return WithOpenConnectionAsync((connection, token) => ReadHitsAsync(connection, query, token), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> LoadBodiesAsync(
        IReadOnlyCollection<Guid> markdownFileIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(markdownFileIds);
        if (markdownFileIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await WithOpenConnectionAsync(
            (connection, token) =>
            {
                SqliteCommand command = connection.CreateCommand();
                command.CommandText = SelectBodiesByIdsSql;
                _ = command.Parameters.AddWithValue("$ids", IdsAsJson(markdownFileIds));
                return SearchIndexRows.ReadIdToTextAsync(command, token);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<SearchIndexHit>> ReadHitsAsync(
        SqliteConnection connection,
        SearchIndexQuery query,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = CreateSearchCommand(connection, query);
        List<SearchIndexHit> results = [];
        using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            SearchIndexHit? hit = await SearchIndexRows.ReadHitAsync(reader, cancellationToken).ConfigureAwait(false);
            if (hit is not null)
            {
                results.Add(hit);
            }
        }

        return results;
    }

    private static SqliteCommand CreateSearchCommand(SqliteConnection connection, SearchIndexQuery query)
    {
        SqliteCommand command = connection.CreateCommand();
        if (query.PathLikePattern is null)
        {
            command.CommandText = SelectWithoutPathFilterSql;
        }
        else
        {
            command.CommandText = SelectWithPathFilterSql;
            _ = command.Parameters.AddWithValue("$path", query.PathLikePattern);
        }

        _ = command.Parameters.AddWithValue("$match", query.MatchExpression);
        _ = command.Parameters.AddWithValue("$take", query.Take);
        _ = command.Parameters.AddWithValue("$skip", query.Skip);
        _ = command.Parameters.AddWithValue("$snippetTokens", query.SnippetTokenCount);
        _ = command.Parameters.AddWithValue("$wTitle", query.TitleWeight);
        _ = command.Parameters.AddWithValue("$wBody", query.BodyWeight);
        _ = command.Parameters.AddWithValue("$wTags", query.TagsWeight);
        _ = command.Parameters.AddWithValue("$wFrontmatter", query.FrontmatterWeight);
        return command;
    }

    private static async Task<int> ApplyInTransactionAsync(
        SqliteConnection connection,
        IReadOnlyCollection<Guid> deletes,
        IReadOnlyCollection<SearchIndexEntry> upserts,
        CancellationToken cancellationToken)
    {
        SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            // FTS5 kennt keinen eindeutigen Schlüssel und kein Upsert. Ein Eintrag wird ersetzt,
            // indem die alte Zeile erst gelöscht und die neue dann eingefügt wird; ohne das Löschen
            // stünde die Datei danach zweimal im Index. Gelöschte und ersetzte Kennungen gehen
            // deshalb in dieselbe Löschanweisung.
            Guid[] idsToRemove = [.. deletes, .. upserts.Select(entry => entry.MarkdownFileId)];
            await DeleteAsync(connection, transaction, idsToRemove, cancellationToken).ConfigureAwait(false);

            foreach (SearchIndexEntry entry in upserts)
            {
                await InsertAsync(connection, transaction, entry, cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return idsToRemove.Length + upserts.Count;
        }
    }

    private static async Task DeleteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid[] fileIds,
        CancellationToken cancellationToken)
    {
        if (fileIds.Length == 0)
        {
            return;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = DeleteByIdsSql;
        command.Transaction = transaction;
        _ = command.Parameters.AddWithValue("$ids", IdsAsJson(fileIds));
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SearchIndexEntry entry,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = InsertSql;
        command.Transaction = transaction;
        _ = command.Parameters.AddWithValue("$title", entry.Title);
        _ = command.Parameters.AddWithValue("$body", entry.Body);
        _ = command.Parameters.AddWithValue("$tags", entry.Tags);
        _ = command.Parameters.AddWithValue("$frontmatter", entry.Frontmatter);
        _ = command.Parameters.AddWithValue("$path", entry.Path);
        _ = command.Parameters.AddWithValue("$fileId", FormatGuid(entry.MarkdownFileId));
        _ = command.Parameters.AddWithValue("$hash", entry.SourceContentHash);
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Konvertiert eine <see cref="Guid"/> in das Textformat, das auch EF Core in SQLite-TEXT-Spalten
    /// schreibt: D-Form mit Bindestrichen, ohne Klammern, in Großbuchstaben. Die Spalte
    /// <c>MarkdownFileId</c> vergleicht mit BINARY-Collation, also schreibungsabhängig. Eine klein
    /// geschriebene Id fände beim Löschen die eigene Zeile nicht, und die Datei stünde doppelt im Index.
    /// </summary>
    private static string FormatGuid(Guid value) =>
        value.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant();

    private static string IdsAsJson(IEnumerable<Guid> ids) =>
        JsonSerializer.Serialize(ids.Select(FormatGuid).ToArray());

    /// <summary>
    /// Öffnet die Verbindung über den Kontext und führt <paramref name="operation"/> innerhalb der
    /// Wiederholungsstrategie aus.
    /// </summary>
    /// <remarks>
    /// Eine Wiederholung führt die ganze Operation erneut aus; Schreibvorgänge laufen dafür in einer
    /// eigenen Transaktion, die bei einem Fehler zurückgerollt ist. <c>CloseConnectionAsync</c>
    /// schließt nur, was der Kontext selbst geöffnet hat; eine schon offene Verbindung bleibt offen.
    /// </remarks>
    private Task<T> WithOpenConnectionAsync<T>(
        Func<SqliteConnection, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        IExecutionStrategy strategy = _dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(
            async token =>
            {
                await _dbContext.Database.OpenConnectionAsync(token).ConfigureAwait(false);
                try
                {
                    return await operation((SqliteConnection)_dbContext.Database.GetDbConnection(), token).ConfigureAwait(false);
                }
                finally
                {
                    await _dbContext.Database.CloseConnectionAsync().ConfigureAwait(false);
                }
            },
            cancellationToken);
    }
}
