using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using MdExplorer.App.Hosting;
using MdExplorer.App.Logging;
using MdExplorer.App.ViewModels;
using MdExplorer.App.Views;
using MdExplorer.Core.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

namespace MdExplorer.App;

/// <summary>
/// WPF-Einstiegspunkt. Verwaltet Generic Host, SplashWindow und Hauptfenster.
/// Klasse ist intern — kein CA1724-Konflikt mit dem Namespace, weil sie nicht öffentlich exportiert wird.
/// </summary>
internal sealed partial class App : System.Windows.Application
{
    private IHost? _host;
    private SplashWindow? _splashWindow;
    private ILogger<App>? _logger;

    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        CultureInfo german = CultureInfo.GetCultureInfo("de-DE");
        Thread.CurrentThread.CurrentCulture = german;
        Thread.CurrentThread.CurrentUICulture = german;

        Log.Logger = SerilogConfiguration.BuildLogger();

        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            await StartHostAndShowMainWindowAsync().ConfigureAwait(true);
        }
        catch (OptionsValidationException exception)
        {
            HandleStartupFailure(exception, "Konfiguration ungültig.");
        }
        catch (ValidationException exception)
        {
            HandleStartupFailure(exception, "Konfigurationsvalidierung fehlgeschlagen.");
        }
        catch (InvalidOperationException exception)
        {
            HandleStartupFailure(exception, "DI- oder Host-Konfiguration fehlerhaft.");
        }
        catch (DbException exception)
        {
            HandleStartupFailure(exception, "Datenbankzugriff beim Start fehlgeschlagen.");
        }
        catch (IOException exception)
        {
            HandleStartupFailure(exception, "Datei- oder Verzeichniszugriff beim Start fehlgeschlagen.");
        }
        catch (UnauthorizedAccessException exception)
        {
            HandleStartupFailure(exception, "Zugriff auf Anwendungspfad verweigert.");
        }
    }

    /// <inheritdoc />
    protected override async void OnExit(ExitEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            _host.Dispose();
            _host = null;
        }

        await Log.CloseAndFlushAsync().ConfigureAwait(true);
        base.OnExit(e);
    }

    private async Task StartHostAndShowMainWindowAsync()
    {
        _host = AppHostBuilder.Build();
        _logger = _host.Services.GetRequiredService<ILogger<App>>();

        _splashWindow = _host.Services.GetRequiredService<SplashWindow>();
        SplashViewModel splashViewModel = _host.Services.GetRequiredService<SplashViewModel>();
        _splashWindow.DataContext = splashViewModel;
        _splashWindow.Show();

        // Schema-Migration muss VOR _host.StartAsync laufen, weil BackgroundServices
        // (ParseOrchestrator, Fts5IndexMaintainer) sofort beim Start lesen — auf
        // einer frischen Datenbank würden sie sonst auf fehlende Tabellen treffen.
        splashViewModel.StatusText = "Datenbank wird vorbereitet …";
        AppInitializer initializer = _host.Services.GetRequiredService<AppInitializer>();
        CancellationToken stoppingToken = _host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        await initializer.InitializeAsync(AppInitializer.DefaultMinimumDisplayDuration, stoppingToken).ConfigureAwait(true);

        await _host.StartAsync(stoppingToken).ConfigureAwait(true);

        MainWindow mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        _splashWindow.Close();
        _splashWindow = null;
    }

    private void HandleStartupFailure(Exception exception, string userFacingHint)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(userFacingHint);

        Log.Fatal(exception, "Anwendung konnte nicht gestartet werden: {Hint}", userFacingHint);

        try
        {
            _splashWindow?.Close();
        }
        catch (InvalidOperationException)
        {
            // SplashWindow war noch nicht initialisiert — ignorierbar.
        }

        _ = MessageBox.Show(
            $"MdExplorer konnte nicht gestartet werden.\n\n{userFacingHint}\nDetails siehe Log-Verzeichnis.",
            "Startfehler",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        Shutdown(exitCode: 1);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (_logger is not null)
        {
            LogDispatcherException(_logger, args.Exception);
        }
        Log.Error(args.Exception, "Unbehandelte Ausnahme im UI-Thread.");
        ShowUnhandledExceptionDialog(args.Exception, isTerminating: false);
        args.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.ExceptionObject is not Exception exception)
        {
            return;
        }
        if (_logger is not null)
        {
            LogAppDomainException(_logger, exception, args.IsTerminating);
        }
        Log.Fatal(exception, "Unbehandelte AppDomain-Ausnahme. Terminierend: {Terminating}.", args.IsTerminating);
        if (args.IsTerminating)
        {
            ShowUnhandledExceptionDialog(exception, isTerminating: true);
        }
    }

    private void ShowUnhandledExceptionDialog(Exception exception, bool isTerminating)
    {
        // Dialog soll auf dem UI-Thread laufen — Background-Threads dispatchen synchron.
        if (Dispatcher is null)
        {
            return;
        }
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowUnhandledExceptionDialog(exception, isTerminating));
            return;
        }

        string title = isTerminating ? "MdExplorer wird beendet" : "Unerwarteter Fehler";
        string hint = isTerminating
            ? "Ein schwerer Fehler hat MdExplorer zum Abbruch gezwungen."
            : "Die Anwendung hat einen Fehler abgefangen und versucht, weiterzulaufen.";
        string message =
            $"{hint}\n\n" +
            $"Fehler: {exception.GetType().Name}\n" +
            $"Details: {exception.Message}\n\n" +
            "Vollständiger Stack-Trace und Kontext stehen im Log-Verzeichnis.";

        _ = MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (_logger is not null)
        {
            LogUnobservedTaskException(_logger, args.Exception);
        }
        Log.Error(args.Exception, "Unbeobachtete Task-Ausnahme.");
        args.SetObserved();
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Error, Message = "Unbehandelte Ausnahme im UI-Thread.")]
    private static partial void LogDispatcherException(Microsoft.Extensions.Logging.ILogger logger, Exception exception);

    [LoggerMessage(EventId = 101, Level = LogLevel.Critical, Message = "Unbehandelte AppDomain-Ausnahme. Terminierend: {Terminating}.")]
    private static partial void LogAppDomainException(Microsoft.Extensions.Logging.ILogger logger, Exception exception, bool terminating);

    [LoggerMessage(EventId = 102, Level = LogLevel.Error, Message = "Unbeobachtete Task-Ausnahme.")]
    private static partial void LogUnobservedTaskException(Microsoft.Extensions.Logging.ILogger logger, Exception exception);
}
