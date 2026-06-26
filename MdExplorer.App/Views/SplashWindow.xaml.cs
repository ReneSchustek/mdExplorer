using System.Windows;

namespace MdExplorer.App.Views;

/// <summary>
/// SplashWindow der Anwendung. Wird beim Start angezeigt und nach Abschluss
/// der Initialisierung geschlossen. Mindestanzeigedauer und Lebenszyklus
/// werden in <c>App.xaml.cs</c> gesteuert.
/// </summary>
internal sealed partial class SplashWindow : Window
{
    /// <summary>Erstellt das SplashWindow.</summary>
    public SplashWindow()
    {
        InitializeComponent();
    }
}
