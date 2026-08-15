namespace MdExplorer.Graph.Models;

/// <summary>
/// Was an einem Dokument hängt — in beide Richtungen.
/// </summary>
/// <remarks>
/// Eine Richtung allein ist eine Sackgasse mit Umweg: Wer nur sieht, wohin ein Dokument
/// verweist, muss für den Rückweg suchen. Der Prüfsatz der Gestaltungslinie lautet, von
/// jedem Ding zu jedem verwandten Ding zu kommen, ohne den Umweg über eine Suche.
/// </remarks>
/// <param name="Outgoing">Dokumente, auf die dieses verweist.</param>
/// <param name="Incoming">Dokumente, die auf dieses verweisen.</param>
public sealed record DocumentRelations(
    IReadOnlyList<RelatedDocument> Outgoing,
    IReadOnlyList<RelatedDocument> Incoming)
{
    /// <summary>Ein Dokument ohne jede Verbindung.</summary>
    public static DocumentRelations Empty { get; } = new([], []);
}

/// <summary>Ein Dokument am anderen Ende einer Verbindung.</summary>
/// <param name="MarkdownFileId">Stabiler Schlüssel — Eingang für das Öffnen.</param>
/// <param name="Title">Dateiname ohne Erweiterung.</param>
/// <param name="RelativePath">Pfad relativ zur Wurzel; unterscheidet gleichnamige Dateien.</param>
public sealed record RelatedDocument(Guid MarkdownFileId, string Title, string RelativePath);
