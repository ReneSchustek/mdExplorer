using MdExplorer.Core.Models;

namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Liest und schreibt die persistente <see cref="AppSettings"/>-Datei.
/// Implementierungen halten den aktuell gültigen Stand und benachrichtigen
/// per <see cref="SettingsChanged"/>-Event über Änderungen — Module konsumieren
/// die Settings live ohne Re-Construction.
/// </summary>
public interface ISettingsService
{
    /// <summary>Der aktuell gültige Settings-Stand. Wird nach <see cref="SaveAsync"/> aktualisiert.</summary>
    AppSettings Current { get; }

    /// <summary>
    /// Wird ausgelöst, wenn <see cref="SaveAsync"/> erfolgreich neue Settings persistiert hat.
    /// Aufrufer können hier Glob-Matcher invalidieren, Re-Scans triggern usw.
    /// </summary>
    event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    /// <summary>
    /// Lädt die Settings aus dem persistierten Speicher. Liefert <see cref="AppSettings.Default"/>,
    /// wenn die Datei fehlt oder ungültig ist (in letzterem Fall mit Warn-Log).
    /// </summary>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persistiert die Settings als JSON. Aktualisiert <see cref="Current"/> und
    /// löst <see cref="SettingsChanged"/> aus, wenn der Stand sich von vorher unterscheidet.
    /// </summary>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}

/// <summary>Eventdaten der <see cref="ISettingsService.SettingsChanged"/>-Benachrichtigung.</summary>
public sealed class SettingsChangedEventArgs : EventArgs
{
    /// <summary>Erzeugt das Event.</summary>
    /// <param name="previous">Vorheriger Stand (oder Defaults, falls das der erste Save ist).</param>
    /// <param name="current">Neuer Stand nach dem Speichern.</param>
    public SettingsChangedEventArgs(AppSettings previous, AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        Previous = previous;
        Current = current;
    }

    /// <summary>Stand vor dem Save.</summary>
    public AppSettings Previous { get; }

    /// <summary>Stand nach dem Save.</summary>
    public AppSettings Current { get; }
}
