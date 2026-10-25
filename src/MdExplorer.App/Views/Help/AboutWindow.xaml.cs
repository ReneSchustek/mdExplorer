using System.Diagnostics.CodeAnalysis;
using System.Windows;
using MdExplorer.App.Services;
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
    private readonly IExternalLinkLauncher _linkLauncher;

    /// <summary>Erzeugt das Fenster und setzt den DataContext.</summary>
    public AboutWindow(AboutViewModel viewModel, IExternalLinkLauncher linkLauncher)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(linkLauncher);
        InitializeComponent();
        _linkLauncher = linkLauncher;
        DataContext = viewModel;
    }

    private void OnCloseClick(object sender, RoutedEventArgs args) => Close();

    /// <summary>
    /// Öffnet das Spendenziel aus <see cref="SupportDonation.PayPalUrl"/> im Standardbrowser.
    /// Scheitert das, bleibt der Dialog offen, und der Grund steht im Protokoll.
    /// </summary>
    private void OnDonationClick(object sender, RoutedEventArgs args) =>
        _linkLauncher.Open(new Uri(SupportDonation.PayPalUrl, UriKind.Absolute));
}
