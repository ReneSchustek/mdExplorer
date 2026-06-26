namespace MdExplorer.Core.Models;

/// <summary>
/// Domain-Entität für eine indizierte Markdown-Datei. Frei von Persistenz-Attributen —
/// das Fluent-API-Mapping liegt in der Data-Schicht, die Persistenz-Schnittstelle ist
/// <see cref="Abstractions.IMarkdownFileRepository"/>.
/// </summary>
public sealed class MarkdownFile
{
    /// <summary>Primärschlüssel der Entität.</summary>
    public Guid Id { get; set; }

    /// <summary>Vollständiger, kanonischer Dateipfad (Unique-Index in der Datenbank).</summary>
    public required string AbsolutePath { get; set; }

    /// <summary>Pfad relativ zur Index-Wurzel, unter der die Datei gefunden wurde.</summary>
    public required string RelativePath { get; set; }

    /// <summary>Dateiname ohne Erweiterung — Anker für Suche und WikiLink-Auflösung.</summary>
    public required string FileNameWithoutExtension { get; set; }

    /// <summary>Größe der Datei in Byte zum Zeitpunkt der letzten Indizierung.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Letzter Schreibzeitpunkt der Datei im UTC-Zeitformat.</summary>
    public DateTime LastWriteTimeUtc { get; set; }

    /// <summary>SHA-256 des Datei-Inhalts als Hex-String — Grundlage für Idempotenz-Prüfung.</summary>
    public required string ContentHash { get; set; }

    /// <summary>Zeitstempel der letzten erfolgreichen Indizierung in UTC.</summary>
    public DateTime IndexedAtUtc { get; set; }
}
