namespace MdExplorer.Data.Repositories;

/// <summary>
/// Verteilt Zuordnungen von Datei und Schlagwort auf eine Liste je Datei. Ein einziger JOIN über
/// Dateien und Schlagworte lieferte jede Datei so oft, wie sie Schlagworte trägt; deshalb kommen
/// die Zuordnungen in einer eigenen Abfrage und werden hier im Speicher einsortiert.
/// </summary>
internal static class TagsByFile
{
    /// <summary>Gruppiert die Zuordnungen nach Datei, in der Reihenfolge, in der sie kommen.</summary>
    /// <param name="rows">Datei-Id und Schlagwort (Name oder Slug).</param>
    /// <returns>Je Datei mit mindestens einer Zuordnung ihre Schlagworte.</returns>
    public static Dictionary<Guid, IReadOnlyList<string>> Group(IEnumerable<(Guid FileId, string Value)> rows)
    {
        Dictionary<Guid, List<string>> buckets = [];
        foreach ((Guid fileId, string value) in rows)
        {
            if (!buckets.TryGetValue(fileId, out List<string>? bucket))
            {
                bucket = [];
                buckets[fileId] = bucket;
            }
            bucket.Add(value);
        }

        Dictionary<Guid, IReadOnlyList<string>> result = new(buckets.Count);
        foreach (KeyValuePair<Guid, List<string>> entry in buckets)
        {
            result[entry.Key] = entry.Value;
        }
        return result;
    }
}
