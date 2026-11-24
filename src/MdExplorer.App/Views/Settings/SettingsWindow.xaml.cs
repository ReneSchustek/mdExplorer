using System.Diagnostics.CodeAnalysis;
using System.Windows;
using MdExplorer.App.Services.Help;
using MdExplorer.App.ViewModels.Settings;

namespace MdExplorer.App.Views.Settings;

/// <summary>
/// Settings-Fenster. Reagiert auf <see cref="SettingsWindowViewModel.CloseRequested"/>
/// und setzt darauf <see cref="Window.DialogResult"/>, damit der Aufrufer den
/// Save/Cancel-Pfad unterscheiden kann.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed partial class SettingsWindow : Window
{
    private readonly SettingsWindowViewModel _viewModel;
    private readonly IHelpContextProvider _helpContextProvider;
    private string? _previousHelpSlug;

    /// <summary>Erzeugt das Fenster und bindet das ViewModel an den DataContext.</summary>
    public SettingsWindow(SettingsWindowViewModel viewModel, IHelpContextProvider helpContextProvider)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(helpContextProvider);
        InitializeComponent();
        _viewModel = viewModel;
        _helpContextProvider = helpContextProvider;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        viewModel.InstallerStarted += OnInstallerStarted;
        Loaded += OnLoadedHandler;
        Closed += OnWindowClosed;
    }

    /// <summary>
    /// Beendet die Anwendung, sobald das Installationsprogramm läuft. Ohne das Beenden
    /// kann der Installer die noch geöffneten Programmdateien nicht ersetzen.
    /// </summary>
    /// <remarks>
    /// Über das Hauptfenster, nicht über <c>Application.Shutdown</c>: Nur so fragt es nach
    /// ungespeicherten Änderungen und stoppt die Dienste geordnet. Bleibt der Nutzer bei der Rückfrage
    /// im Programm, läuft es weiter, und das Installationsprogramm meldet die geöffnete Anwendung
    /// selbst. Erst nach dem Dialog: Das Setzen von <see cref="Window.DialogResult"/> schließt ihn,
    /// und sein <c>ShowDialog</c> soll zurückgekehrt sein, bevor das Hauptfenster schließt.
    /// </remarks>
    private void OnInstallerStarted(object? sender, EventArgs args)
    {
        DialogResult = false;
        _ = Dispatcher.BeginInvoke(() => Application.Current?.MainWindow?.Close());
    }

    private void OnLoadedHandler(object sender, RoutedEventArgs args)
    {
        _previousHelpSlug = _helpContextProvider.CurrentSlug;
        _helpContextProvider.SetSlug(HelpContext.Indexing);
    }

    private void OnCloseRequested(object? sender, SettingsCloseEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        // Das Setzen schließt den modalen Dialog.
        DialogResult = args.SavedChanges;
    }

    private void OnWindowClosed(object? sender, EventArgs args)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.InstallerStarted -= OnInstallerStarted;
        Loaded -= OnLoadedHandler;
        Closed -= OnWindowClosed;
        // Modaler Dialog: nach dem Schließen den vorherigen Kontext zurückgeben,
        // damit F1 wieder dort weitermacht, wo der Anwender vor dem Dialog war.
        if (_previousHelpSlug is not null)
        {
            _helpContextProvider.SetSlug(_previousHelpSlug);
        }
    }
}
