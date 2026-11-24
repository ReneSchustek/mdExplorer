namespace MdExplorer.Graph.Models;

/// <summary>Ein Dokument am anderen Ende einer Verbindung.</summary>
/// <param name="MarkdownFileId">Stabiler Schlüssel als Eingang für das Öffnen.</param>
/// <param name="Title">Dateiname ohne Erweiterung.</param>
/// <param name="RelativePath">Pfad relativ zur Wurzel; unterscheidet gleichnamige Dateien.</param>
public sealed record RelatedDocument(Guid MarkdownFileId, string Title, string RelativePath);
