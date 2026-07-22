namespace MdExplorer.App.Logging;

/// <summary>
/// Lesender Zugriff auf den In-Memory-Log-Puffer (<see cref="MemorySink"/>).
/// Stellt UI- und Test-Komponenten einen Snapshot und einen Live-Stream
/// zur Verfügung, ohne die Sink-Implementierung an Serilog zu koppeln.
/// </summary>
internal interface IMemoryLogStore
{
    /// <summary>Maximale Anzahl gleichzeitig gepufferter Einträge.</summary>
    int Capacity { get; }

    /// <summary>Wird unmittelbar nach jedem hinzugefügten Eintrag gefeuert.</summary>
    /// <remarks>
    /// Handler werden auf dem schreibenden Thread aufgerufen — UI-Konsumenten
    /// müssen selbst auf den Dispatcher marshallen.
    /// </remarks>
    event EventHandler<LogEntry>? EntryAdded;

    /// <summary>Liefert eine Momentaufnahme aller gehaltenen Einträge in Eingangsreihenfolge.</summary>
    IReadOnlyList<LogEntry> Snapshot();
}
