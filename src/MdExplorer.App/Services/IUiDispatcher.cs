namespace MdExplorer.App.Services;

/// <summary>
/// Abstrahiert den UI-Dispatcher gegen einen synchronen In-Test-Dispatcher,
/// damit ViewModels Marshalling testbar bleiben.
/// </summary>
internal interface IUiDispatcher
{
    /// <summary>
    /// Führt <paramref name="action"/> auf dem UI-Thread aus. Synchron, wenn der
    /// aufrufende Thread bereits der UI-Thread ist; sonst über den Dispatcher.
    /// </summary>
    void Invoke(Action action);

    /// <summary>
    /// Stellt <paramref name="action"/> hinter die laufende Arbeit des UI-Threads, statt sie sofort
    /// auszuführen.
    /// </summary>
    /// <remarks>
    /// Für Rückmeldungen an eine Bindung, die gerade selbst einen Wert überträgt: Solange eine
    /// Auswahlliste ihre neue Auswahl an das ViewModel weitergibt, übernimmt sie einen
    /// zurückgesetzten Wert aus der Quelle nicht.
    /// </remarks>
    void Post(Action action);
}
