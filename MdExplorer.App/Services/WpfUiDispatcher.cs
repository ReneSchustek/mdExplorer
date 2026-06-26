using System.Windows;
using System.Windows.Threading;

namespace MdExplorer.App.Services;

/// <summary>
/// Standard-WPF-Implementierung von <see cref="IUiDispatcher"/>. Greift auf
/// den <see cref="Application.Current"/>-Dispatcher zurück oder, falls keiner
/// existiert, auf den aktuellen Thread-Dispatcher.
/// </summary>
internal sealed class WpfUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }
        dispatcher.Invoke(action);
    }
}
