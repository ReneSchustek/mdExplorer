using System.Data;
using System.Data.Common;
using System.Globalization;
using MdExplorer.Core.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Repositories;

/// <summary>
/// SQLite/FTS5-Implementierung von <see cref="ISearchIndexStorage"/>. Kapselt das gesamte
/// SQL der <c>MarkdownSearchIndex</c>-Tabelle, damit das Search-Modul weder EF Core noch
/// <see cref="SqliteConnection"/> direkt kennt. Die Verbindung wird aus dem
/// <see cref="MdExplorerDbContext"/> entliehen — analog zum bisherigen Maintainer-Verhalten.
/// </summary>
public sealed class SqliteSearchIndexStorage : ISearchIndexStorage
{
    private const string SelectStateSql = """
        SELECT "MarkdownFileId", "SourceContentHash"
        FROM "MarkdownSearchIndex";
        """;

    private const string SelectBodyByIdSql = """
        SELECT "MarkdownFileId", "Body"
        FROM "MarkdownSearchIndex"
        WHERE "MarkdownFileId" = $fileId;
        """;

    private const string DeleteByFileIdSql = """
        DELETE FROM "MarkdownSearchIndex"
        WHERE "MarkdownFileId" = $fileId;
        """;

    private const string InsertSql = """
        INSERT INTO "MarkdownSearchIndex"
            ("Title", "Body", "Tags", "Frontmatter", "Path", "MarkdownFileId", "SourceContentHash")
        VALUES
            ($title, $body, $tags, $frontmatter, $path, $fileId, $hash);
        """;

    // Gemeinsamer SELECT-Rumpf für die FTS5-Suche. Der WHERE-Block wird je nach
    // Path-Filter unterschiedlich kombiniert; alle Werte bleiben parametrisiert.
    private const string SelectSearchPrefix =
        "SELECT \"MarkdownFileId\", \"Path\", \"Title\", " +
        "snippet(\"MarkdownSearchIndex\", 1, '<mark>', '</mark>', '…', $snippetTokens) AS \"Snippet\", " +
        "bm25(\"MarkdownSearchIndex\", $wTitle, $wBody, $wTags, $wFrontmatter) AS \"Score\" " +
        "FROM \"MarkdownSearchIndex\" " +
        "WHERE \"MarkdownSearchIndex\" MATCH $match";

    private const string SelectSearchPathFilter = " AND \"Path\" LIKE $path";

    private const string SelectSearchSuffix = " ORDER BY \"Score\" ASC LIMIT $take OFFSET $skip;";

    // Beide vollständigen Statements werden vom Compiler aus den Konstanten zusammengesetzt.
    // CA2100 ist damit zufrieden, weil keine Laufzeit-Konkatenation erfolgt.
    private const string SelectWithoutPathFilterSql = SelectSearchPrefix + SelectSearchSuffix;

    private const string SelectWithPathFilterSql = SelectSearchPrefix + SelectSearchPathFilter + SelectSearchSuffix;

    private readonly MdExplorerDbContext _dbContext;

    /// <summary>Erzeugt den Storage und injiziert den DbContext.</summary>
    public SqliteSearchIndexStorage(MdExplorerDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> LoadIndexedHashesAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = (SqliteConnection)_dbContext.Database.GetDbConnection();
        bool openedHere = await OpenIfNeededAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<Guid, string> result = [];
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = SelectStateSql;
            using DbDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await reader.IsDBNullAsync(0, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }
                Guid id = Guid.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
                string hash = await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false)
                    ? string.Empty
                    : reader.GetString(1);
                result[id] = hash;
            }
            return result;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

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

        SqliteConnection connection = (SqliteConnection)_dbContext.Database.GetDbConnection();
        bool openedHere = await OpenIfNeededAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                foreach (Guid orphan in deletes)
                {
                    await DeleteByFileIdAsync(connection, transaction, orphan, cancellationToken).ConfigureAwait(false);
                }

                foreach (SearchIndexEntry entry in upserts)
                {
                    await DeleteByFileIdAsync(connection, transaction, entry.MarkdownFileId, cancellationToken).ConfigureAwait(false);
                    await InsertAsync(connection, transaction, entry, cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchIndexHit>> QueryAsync(SearchIndexQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        SqliteConnection connection = (SqliteConnection)_dbContext.Database.GetDbConnection();
        bool openedHere = await OpenIfNeededAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            using SqliteCommand command = connection.CreateCommand();
            if (query.PathLikePattern is null)
            {
                command.CommandText = SelectWithoutPathFilterSql;
            }
            else
            {
                command.CommandText = SelectWithPathFilterSql;
            }
            _ = command.Parameters.AddWithValue("$match", query.MatchExpression);
            _ = command.Parameters.AddWithValue("$take", query.Take);
            _ = command.Parameters.AddWithValue("$skip", query.Skip);
            _ = command.Parameters.AddWithValue("$snippetTokens", query.SnippetTokenCount);
            _ = command.Parameters.AddWithValue("$wTitle", query.TitleWeight);
            _ = command.Parameters.AddWithValue("$wBody", query.BodyWeight);
            _ = command.Parameters.AddWithValue("$wTags", query.TagsWeight);
            _ = command.Parameters.AddWithValue("$wFrontmatter", query.FrontmatterWeight);
            if (query.PathLikePattern is not null)
            {
                _ = command.Parameters.AddWithValue("$path", query.PathLikePattern);
            }

            List<SearchIndexHit> results = [];
            using DbDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Guid markdownFileId = await ReadGuidAsync(reader, 0, cancellationToken).ConfigureAwait(false);
                string path = reader.GetString(1);
                string title = reader.GetString(2);
                string snippet = await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false)
                    ? string.Empty
                    : reader.GetString(3);
                double score = await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false)
                    ? 0.0
                    : reader.GetDouble(4);
                results.Add(new SearchIndexHit(markdownFileId, path, title, snippet, score));
            }
            return results;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
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

        SqliteConnection connection = (SqliteConnection)_dbContext.Database.GetDbConnection();
        bool openedHere = await OpenIfNeededAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<Guid, string> result = new(markdownFileIds.Count);
            foreach (Guid id in markdownFileIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? body = await LoadBodyByIdAsync(connection, id, cancellationToken).ConfigureAwait(false);
                if (body is not null)
                {
                    result[id] = body;
                }
            }
            return result;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<string?> LoadBodyByIdAsync(SqliteConnection connection, Guid id, CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SelectBodyByIdSql;
        _ = command.Parameters.AddWithValue("$fileId", FormatGuid(id));
        using DbDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }
        return await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false)
            ? string.Empty
            : reader.GetString(1);
    }

    private static async Task<bool> OpenIfNeededAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State == ConnectionState.Open)
        {
            return false;
        }
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task DeleteByFileIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = DeleteByFileIdSql;
        _ = command.Parameters.AddWithValue("$fileId", FormatGuid(fileId));
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SearchIndexEntry entry,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = InsertSql;
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
    /// schreibt: Uppercase-D-Form ohne Trennzeichen-Klammern. Wichtig für den TRIGGER-Vergleich
    /// <c>OLD."MarkdownFileId" = FTS5."MarkdownFileId"</c>, der mit BINARY-Collation case-sensitive ist.
    /// </summary>
    private static string FormatGuid(Guid value) =>
        value.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant();

    private static async Task<Guid> ReadGuidAsync(DbDataReader reader, int ordinal, CancellationToken cancellationToken)
    {
        if (await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false))
        {
            return Guid.Empty;
        }
        string raw = reader.GetString(ordinal);
        return Guid.Parse(raw, CultureInfo.InvariantCulture);
    }
}
