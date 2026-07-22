namespace MdExplorer.Core.Models;

/// <summary>
/// Parser-Ergebnis-Entität, 1:1 zu <see cref="MarkdownFile"/> über <see cref="MarkdownFileId"/>.
/// Hält gerendertes HTML (GZip-komprimiert), Frontmatter und Outlinks.
/// </summary>
public sealed class MarkdownDocument
{
    private byte[] _renderedHtmlGz = [];

    /// <summary>Primärschlüssel.</summary>
    public Guid Id { get; set; }

    /// <summary>Fremdschlüssel auf <see cref="MarkdownFile.Id"/> (Unique — 1:1-Beziehung).</summary>
    public Guid MarkdownFileId { get; set; }

    /// <summary>Navigation auf die zugehoerige <see cref="Models.MarkdownFile"/> — fuer EF-Core-Cascade-Delete und Fluent-Mapping.</summary>
    public MarkdownFile? MarkdownFile { get; set; }

    /// <summary>Hash des Quell-Markdowns zum Parse-Zeitpunkt — Grundlage für Idempotenz-Prüfung.</summary>
    public required string SourceContentHash { get; set; }

    /// <summary>Frontmatter serialisiert als JSON (Schlüssel-Wert-Paare, Werte als String).</summary>
    public required string FrontmatterJson { get; set; }

    /// <summary>GZip-komprimiertes, gerendertes HTML — kapselt das interne <c>byte[]</c> hinter <see cref="ReadOnlyMemory{T}"/>, EF-Mapping greift über das Backing-Field zu.</summary>
    public ReadOnlyMemory<byte> RenderedHtmlGz => _renderedHtmlGz;

    /// <summary>Outlinks (WikiLink-Zielslugs) serialisiert als JSON-Array.</summary>
    public required string OutlinksJson { get; set; }

    /// <summary>Zeitpunkt des letzten erfolgreichen Parses (UTC).</summary>
    public DateTime ParsedAtUtc { get; set; }

    /// <summary>Setzt den HTML-Blob aus einem Span — verzichtet auf ein öffentliches <c>byte[]</c>-Property (CA1819).</summary>
    public void SetRenderedHtmlGz(ReadOnlySpan<byte> bytes)
    {
        _renderedHtmlGz = bytes.ToArray();
    }
}
