using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.App.Hosting;

/// <summary>
/// Gibt einem Nebenfenster einen eigenen Scope, der mit dem Fenster endet.
/// </summary>
/// <remarks>
/// Ohne Scope hielte der Wurzel-Container jede freizugebende Instanz fest, die er für das Fenster
/// erzeugt hat, bis die Anwendung endet. Bei einem Fenster, das man oft öffnet, wächst damit der
/// Speicher mit jedem Öffnen.
/// </remarks>
internal static class WindowScope
{
    /// <summary>
    /// Erzeugt das Fenster in einem neuen Scope und gibt den Scope frei, sobald das Fenster
    /// geschlossen ist.
    /// </summary>
    /// <param name="provider">Container, aus dem der Scope stammt.</param>
    /// <param name="build">Baut das Fenster aus dem Container des Scopes.</param>
    /// <typeparam name="TWindow">Art des Fensters.</typeparam>
    public static TWindow Create<TWindow>(IServiceProvider provider, Func<IServiceProvider, TWindow> build)
        where TWindow : Window
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(build);

        IServiceScope? scope = provider.CreateScope();
        try
        {
            TWindow window = build(scope.ServiceProvider);
            IServiceScope ownedByWindow = scope;
            window.Closed += (_, _) => ownedByWindow.Dispose();
            scope = null;
            return window;
        }
        finally
        {
            // Nur noch gesetzt, wenn das Fenster nicht entstand: Dann gibt es kein Schließen,
            // das den Scope freigäbe.
            scope?.Dispose();
        }
    }
}
