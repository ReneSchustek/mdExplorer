namespace MdExplorer.TagCloud.Models;

/// <summary>
/// Aggregat eines Tags für die Tag-Cloud-Darstellung: Original-Name, normalisierter Slug,
/// Häufigkeit über alle indizierten Dateien sowie Zeitpunkt der zuletzt geänderten Datei
/// mit diesem Tag (UTC). Record für Value-Equality — erleichtert Vergleiche und Tests.
/// </summary>
/// <param name="Name">Original-Tag-Name in Ursprungsschreibweise.</param>
/// <param name="Slug">Eindeutiger, normalisierter Slug (Lowercase, Umlaute erhalten).</param>
/// <param name="Count">Anzahl Dateien, die den Tag tragen — strikt &gt; 0.</param>
/// <param name="LastUsedUtc">
/// MAX(<c>MarkdownFile.LastWriteTimeUtc</c>) über alle Dateien mit diesem Tag (UTC).
/// </param>
public sealed record TagStatistic(string Name, string Slug, int Count, DateTime LastUsedUtc);
