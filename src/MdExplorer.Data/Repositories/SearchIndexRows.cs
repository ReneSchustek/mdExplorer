using System.Data.Common;
using System.Globalization;
using MdExplorer.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace MdExplorer.Data.Repositories;

/// <summary>Liest die Zeilen der Volltext-Abfragen aus, mit einheitlicher Behandlung von NULL.</summary>
internal static class SearchIndexRows
{
    // Liest Zeilen der Form (Kennung, Text). Zeilen ohne Kennung kommen in einer gesunden Tabelle
    // nicht vor, weil jeder Schreibweg sie setzt; sie werden übergangen statt als Guid.Empty
    // weitergereicht. Fehlende Kennungen liefert FTS5 einfach nicht zurück.
    public static async Task<IReadOnlyDictionary<Guid, string>> ReadIdToTextAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        using (command)
        {
            Dictionary<Guid, string> result = [];
            using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Guid? id = await ReadGuidAsync(reader, 0, cancellationToken).ConfigureAwait(false);
                if (id is not null)
                {
                    result[id.Value] = await ReadTextAsync(reader, 1, cancellationToken).ConfigureAwait(false);
                }
            }

            return result;
        }
    }

    // Wie oben: Eine Zeile ohne Kennung wird übergangen. Pfad, Titel und Ausschnitt fallen bei
    // NULL auf eine leere Zeichenkette zurück, die Bewertung auf 0.
    public static async Task<SearchIndexHit?> ReadHitAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        Guid? markdownFileId = await ReadGuidAsync(reader, 0, cancellationToken).ConfigureAwait(false);
        if (markdownFileId is null)
        {
            return null;
        }

        string path = await ReadTextAsync(reader, 1, cancellationToken).ConfigureAwait(false);
        string title = await ReadTextAsync(reader, 2, cancellationToken).ConfigureAwait(false);
        string snippet = await ReadTextAsync(reader, 3, cancellationToken).ConfigureAwait(false);
        double score = await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false) ? 0.0 : reader.GetDouble(4);
        return new SearchIndexHit(markdownFileId.Value, path, title, snippet, score);
    }

    private static async Task<Guid?> ReadGuidAsync(DbDataReader reader, int ordinal, CancellationToken cancellationToken)
    {
        if (await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Guid.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
    }

    private static async Task<string> ReadTextAsync(DbDataReader reader, int ordinal, CancellationToken cancellationToken) =>
        await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false) ? string.Empty : reader.GetString(ordinal);
}
