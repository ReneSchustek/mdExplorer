namespace MdExplorer.Update.Abstractions;

/// <summary>
/// Persistiert den Zeitpunkt der letzten erfolgreichen Update-Prüfung, damit der Check
/// nicht bei jedem Programmstart erneut über das Netz geht (Throttle).
/// </summary>
public interface IUpdateCheckJournal
{
    /// <summary>Liest den Zeitpunkt der letzten erfolgreichen Prüfung (UTC), oder <see langword="null"/>, wenn nie geprüft.</summary>
    /// <param name="cancellationToken">Abbruch-Token.</param>
    Task<DateTimeOffset?> ReadLastCheckAsync(CancellationToken cancellationToken);

    /// <summary>Schreibt den Zeitpunkt der letzten erfolgreichen Prüfung (UTC).</summary>
    /// <param name="timestampUtc">Zeitstempel der Prüfung.</param>
    /// <param name="cancellationToken">Abbruch-Token.</param>
    Task WriteLastCheckAsync(DateTimeOffset timestampUtc, CancellationToken cancellationToken);
}
