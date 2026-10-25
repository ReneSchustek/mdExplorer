namespace MdExplorer.App.ViewModels;

/// <summary>
/// Die eine Stelle, die vor jedem Verlassen des Dokuments im Editor nach ungespeicherten
/// Änderungen fragt: vor dem Laden eines anderen Dokuments, vor Umbenennen, Verschieben und
/// Löschen und vor dem Schließen des Hauptfensters.
/// </summary>
internal interface IUnsavedChangesGuard
{
    /// <summary>Wahr, solange der Editor Änderungen hält, die nicht auf der Platte stehen.</summary>
    bool HasUnsavedChanges { get; }

    /// <summary>Fragt bei ungespeicherten Änderungen nach und führt die Antwort aus.</summary>
    /// <param name="cancellationToken">Abbruch des Speicherns.</param>
    /// <returns>
    /// <see langword="true"/>, wenn der Wechsel stattfinden darf: nichts ungespeichert,
    /// gespeichert oder verworfen. <see langword="false"/> bei Abbrechen oder gescheitertem
    /// Speichern; dann bleibt alles, wie es war.
    /// </returns>
    Task<bool> ConfirmLeaveAsync(CancellationToken cancellationToken);
}
