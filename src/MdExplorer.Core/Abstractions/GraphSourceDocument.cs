namespace MdExplorer.Core.Abstractions;

/// <summary>Minimal-Repräsentation eines geparsten Dokuments.</summary>
/// <param name="MarkdownFileId">Fremdschlüssel auf <see cref="GraphSourceFile.Id"/>.</param>
/// <param name="OutlinksJson">JSON-Array mit den WikiLink-Zielen (Slug-Form).</param>
public sealed record GraphSourceDocument(Guid MarkdownFileId, string OutlinksJson);
