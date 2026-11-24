namespace MdExplorer.Core.Abstractions;

/// <summary>Minimal-Repräsentation eines indizierungsrelevanten Dokuments.</summary>
/// <param name="MarkdownFileId">Fremdschlüssel auf <c>MarkdownFile.Id</c> als Identität des Index-Eintrags.</param>
/// <param name="Title">Titel, entspricht <c>MarkdownFile.FileNameWithoutExtension</c>.</param>
/// <param name="AbsolutePath">Absoluter Dateipfad als Quelle für den Body-Text.</param>
/// <param name="RelativePath">Relativer Pfad, wird als <c>Path</c>-Spalte im FTS5-Index gespeichert.</param>
/// <param name="SourceContentHash">Aktueller Quell-Hash (Idempotenz-Marker).</param>
/// <param name="FrontmatterJson">Frontmatter-JSON aus dem Parser. Wird im Maintainer flachgeklopft.</param>
public sealed record SearchSourceDocument(
    Guid MarkdownFileId,
    string Title,
    string AbsolutePath,
    string RelativePath,
    string SourceContentHash,
    string FrontmatterJson);
