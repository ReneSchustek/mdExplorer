namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Vertrag für das Anwenden ausstehender Datenbank-Migrationen.
/// Konkrete Implementierung lebt in <c>MdExplorer.Data</c>.
/// </summary>
public interface IDatabaseMigrator
{
    /// <summary>
    /// Wendet alle ausstehenden Migrationen idempotent an.
    /// Tut nichts, falls die Datenbank aktuell ist.
    /// </summary>
    Task MigrateAsync(CancellationToken cancellationToken);
}
