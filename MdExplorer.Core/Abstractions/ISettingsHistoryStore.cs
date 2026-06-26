using MdExplorer.Core.Models;

namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Persistiert pro Save-Operation:
/// (1) einen vollständigen JSON-Snapshot des neuen Settings-Stands und
/// (2) einen Diff-Eintrag im Audit-Log (Was, Wann, Welche Werte).
/// Entspricht der Pflicht aus <c>_global/rules/auditability.md</c>.
/// </summary>
public interface ISettingsHistoryStore
{
    /// <summary>
    /// Schreibt einen Snapshot der neuen Settings und appendet den Diff-Eintrag.
    /// Aufruf erfolgt nach erfolgreichem Persistieren der Settings-Datei.
    /// </summary>
    /// <param name="previous">Stand vor dem Save.</param>
    /// <param name="current">Stand nach dem Save.</param>
    /// <param name="previousJson">Serialisierte Form von <paramref name="previous"/> — Basis für den Diff.</param>
    /// <param name="currentJson">Serialisierte Form von <paramref name="current"/> — wird identisch in die Snapshot-Datei geschrieben.</param>
    /// <param name="timestamp">Festgehaltener Zeitpunkt (UTC). Wird sowohl im Snapshot-Dateinamen als auch im Audit-Log gespeichert.</param>
    /// <param name="cancellationToken">Abbrechbar bei Host-Stop.</param>
    Task RecordAsync(
        AppSettings previous,
        AppSettings current,
        string previousJson,
        string currentJson,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken);
}
