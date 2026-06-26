namespace MdExplorer.Parser.Models;

/// <summary>
/// Ergebnis eines einzelnen Parse-Laufs. Wird vom <c>ParseOrchestrator</c> in die
/// <c>MdExplorer.Core.Models.MarkdownDocument</c>-, <c>Tag</c>- und <c>MarkdownFileTag</c>-Entitäten aufgelöst.
/// </summary>
/// <param name="Frontmatter">Geflachte Frontmatter-Schlüssel-Wert-Paare (Werte als String).</param>
/// <param name="Tags">Liste der eindeutigen, normalisierten Tag-Slugs (Body + Frontmatter zusammengefasst).</param>
/// <param name="TagNames">Original-Tag-Namen, korrespondierend zu <paramref name="Tags"/> (gleicher Index).</param>
/// <param name="OutlinkSlugs">Eindeutige, normalisierte WikiLink-Ziel-Slugs.</param>
/// <param name="RenderedHtmlGz">GZip-komprimiertes, gerendertes HTML (über <see cref="ReadOnlyMemory{T}"/>, vermeidet CA1819).</param>
public sealed record ParseResult(
    IReadOnlyDictionary<string, string> Frontmatter,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> TagNames,
    IReadOnlyList<string> OutlinkSlugs,
    ReadOnlyMemory<byte> RenderedHtmlGz);
