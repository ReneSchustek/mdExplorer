namespace MdExplorer.Data.Repositories;

/// <summary>
/// Gemeinsame Portionsgröße für Abfragen, die eine Liste von Schlüsseln als IN-Liste schicken.
/// </summary>
/// <remarks>
/// EF Core übersetzt <c>liste.Contains(spalte)</c> in eine IN-Liste mit einem gebundenen Parameter
/// je Wert. SQLite vor Version 3.32 ließ höchstens 999 solcher Parameter je Anweisung zu, die
/// mitgelieferte Fassung 32.766. 500 bleibt mit Abstand unter der alten Grenze, auch wenn eine
/// ältere SQLite-Bibliothek geladen wird, und hält jede Portion klein genug, dass ein Abbruch nur
/// wenig Arbeit kostet. Die Tests prüfen das Portionieren gegen die alte Grenze.
/// </remarks>
internal static class SqliteInList
{
    /// <summary>Höchstzahl der Schlüssel je Anweisung.</summary>
    public const int BatchSize = 500;
}
