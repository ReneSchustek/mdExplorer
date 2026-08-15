namespace MdExplorer.App.Services;

/// <summary>
/// Führt Umbenennen, Verschieben und Löschen eines Dokuments aus — Datei und Index zusammen.
/// </summary>
/// <remarks>
/// Beides gehört zusammen, weil beides auseinanderlaufen kann: Eine umbenannte Datei, deren
/// Eintrag im Index noch den alten Pfad trägt, führt jede Liste ins Leere. Deshalb nicht zwei
/// Aufrufe an zwei Stellen, sondern ein Vorgang mit einer Zusage.
/// </remarks>
internal interface IDocumentFileService
{
    /// <summary>
    /// Was ein Eingriff an dieser Datei nach sich zöge.
    /// </summary>
    /// <remarks>
    /// Vor dem Klick, nicht danach: Wer erst hinterher erfährt, dass sieben Dokumente ins
    /// Leere zeigen, kann es nicht mehr abwählen. Gilt für das Löschen wie fürs Umbenennen —
    /// ein WikiLink zeigt auf den Dateinamen, und der ändert sich bei beidem.
    /// </remarks>
    Task<DocumentImpact> GetImpactAsync(Guid markdownFileId, CancellationToken cancellationToken);

    /// <summary>Benennt die Datei um; der Ordner bleibt derselbe.</summary>
    /// <param name="markdownFileId">Das Dokument.</param>
    /// <param name="newFileName">Neuer Name — mit oder ohne Erweiterung <c>.md</c>.</param>
    /// <param name="cancellationToken">Abbruchmerker.</param>
    Task<DocumentFileResult> RenameAsync(Guid markdownFileId, string newFileName, CancellationToken cancellationToken);

    /// <summary>Verschiebt die Datei in ein anderes Verzeichnis; der Name bleibt derselbe.</summary>
    /// <param name="markdownFileId">Das Dokument.</param>
    /// <param name="targetDirectory">Zielverzeichnis als absoluter Pfad.</param>
    /// <param name="cancellationToken">Abbruchmerker.</param>
    Task<DocumentFileResult> MoveAsync(Guid markdownFileId, string targetDirectory, CancellationToken cancellationToken);

    /// <summary>Löscht die Datei und nimmt sie aus dem Index.</summary>
    /// <param name="markdownFileId">Das Dokument.</param>
    /// <param name="cancellationToken">Abbruchmerker.</param>
    Task<DocumentFileResult> DeleteAsync(Guid markdownFileId, CancellationToken cancellationToken);
}

/// <summary>Was ein Eingriff an einem Dokument bei den anderen anrichtet.</summary>
/// <param name="Title">Name des Dokuments, wie er in der Rückfrage steht.</param>
/// <param name="IncomingLinkCount">Anzahl der Dokumente, deren Verweise ins Leere zeigen würden.</param>
internal sealed record DocumentImpact(string Title, int IncomingLinkCount)
{
    /// <summary>Ein Dokument, das es nicht gibt.</summary>
    public static DocumentImpact Unknown { get; } = new(string.Empty, 0);
}

/// <summary>Ausgang eines Vorgangs an Datei und Index.</summary>
/// <param name="Succeeded">Ob der Vorgang vollständig durchlief.</param>
/// <param name="Message">Was passiert ist — für die Statuszeile, in ganzen Sätzen.</param>
/// <param name="NewAbsolutePath">Der neue Pfad, sofern die Datei noch existiert.</param>
internal sealed record DocumentFileResult(bool Succeeded, string Message, string? NewAbsolutePath)
{
    /// <summary>Erzeugt ein Fehlschlag-Ergebnis mit Begründung.</summary>
    public static DocumentFileResult Failed(string message) => new(false, message, null);
}
