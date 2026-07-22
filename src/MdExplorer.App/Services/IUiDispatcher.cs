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
}
