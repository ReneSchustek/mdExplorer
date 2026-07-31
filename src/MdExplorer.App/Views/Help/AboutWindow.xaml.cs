using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using MdExplorer.App.Services.Help;
using MdExplorer.App.ViewModels.Help;

namespace MdExplorer.App.Views.Help;

/// <summary>
/// „Über MdExplorer…"-Dialog. Modal, schließt sich über die Esc-Taste oder den
/// „Schließen"-Button. Bindet das <see cref="AboutViewModel"/> einmalig im
/// Konstruktor; ein Refresh zur Laufzeit ist nicht vorgesehen.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed partial class AboutWindow : Window
{
    /// <summary>Erzeugt das Fenster und setzt den DataContext.</summary>
    public AboutWindow(AboutViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCloseClick(object sender, RoutedEventArgs args) => Close();

    /// <summary>
    /// Öffnet das Spendenziel im Standardbrowser. Die Adresse stammt aus
    /// <see cref="SupportDonation.PayPalUrl"/> und ist eine Compile-Zeit-Konstante auf
    /// HTTPS — es gibt keinen Laufzeit-Pfad, über den hier eine fremde URL ankommen
    /// könnte, weshalb eine zusätzliche Schema-Prüfung ins Leere liefe.
    /// </summary>
    private void OnDonationClick(object sender, RoutedEventArgs args)
    {
        try
        {
            using Process? process = Process.Start(
                new ProcessStartInfo(SupportDonation.PayPalUrl) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or System.IO.FileNotFoundException)
        {
            // Kein Standardbrowser oder Start verweigert — der Dialog bleibt offen, der
            // Nutzer kann die Adresse manuell aufrufen. Bewusst nicht-fatal: eine
            // freiwillige Spende ist kein Grund, die Anwendung zu beenden.
        }
    }
}
