using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using MdExplorer.App.Hosting;
using MdExplorer.App.Localization;
using MdExplorer.App.Logging;
using MdExplorer.App.Services;
using MdExplorer.App.ViewModels;
using MdExplorer.App.Views;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MdExplorer.App;

/// <summary>
/// WPF-Einstiegspunkt: startet Protokoll, Generic Host und Datenbank, zeigt erst das Startbild
/// und dann das Hauptfenster und fängt Ausnahmen ab, die sonst niemand mehr fängt. Läuft schon
/// eine Instanz des Benutzers, reicht diese ihr die Datei aus der Befehlszeile und endet. Weil die
/// Klasse intern ist, greift CA1724 (Name gleich Namespace) hier nicht.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed partial class App : System.Windows.Application
{
    // Höchstens so lange für die Hintergrunddienste; danach schließt das Fenster auch dann, wenn
    // einer noch hängt.
    private static readonly TimeSpan ServiceStopTimeout = TimeSpan.FromSeconds(5);

    private readonly UnhandledExceptionPolicy _exceptionPolicy = new();
    private IHost? _host;
    private HostShutdown? _shutdown;
    private SplashWindow? _splashWindow;
    private ILogger<App>? _logger;
    private FirstInstance? _firstInstance;

    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        UseGermanCulture();
        Log.Logger = SerilogConfiguration.BuildLogger();

        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Hier steht bewusst kein catch. Eine Ausnahme aus dem Start fällt über async void in den
        // Dispatcher und kommt in OnDispatcherUnhandledException an, gleich welcher Art sie ist.
        // Bis MarkStartupCompleted beendet sie dort die Anwendung, statt ein Startbild ohne
        // Hauptfenster stehen zu lassen.
        // Der Instanzentscheid steht vor dem Host: Eine zweite Instanz darf weder die Datenbank
        // öffnen noch Hintergrunddienste starten. Ist diese die erste, steht danach das Feld.
        if (!await ClaimFirstInstanceAsync(e.Args).ConfigureAwait(true) || _firstInstance is null)
        {
            return;
        }
        await StartHostAndShowMainWindowAsync().ConfigureAwait(true);
        _exceptionPolicy.MarkStartupCompleted();
        ConnectOpenRequests(_firstInstance);
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // Die Dienste sind hier schon gestoppt, das hat das Hauptfenster vor dem Schließen
        // abgewartet (HostShutdown). Nach einem gescheiterten Start nicht: Dann gibt Dispose sie
        // ohne geordnetes Stoppen frei, und der Prozess endet ohnehin.
        _host?.Dispose();
        _host = null;
        _firstInstance?.Dispose();
        _firstInstance = null;
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    // Für alle Threads, nicht nur diesen: Was im Hintergrund formatiert wird (Zahlen und Daten in
    // Meldungen), soll dieselbe Kultur sehen wie die Oberfläche. Die Anwendung gibt es nur auf
    // Deutsch.
    private static void UseGermanCulture()
    {
        CultureInfo german = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = german;
        CultureInfo.DefaultThreadCurrentUICulture = german;
        Thread.CurrentThread.CurrentCulture = german;
        Thread.CurrentThread.CurrentUICulture = german;
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Error, Message = "Unbehandelte Ausnahme im UI-Thread.")]
    private static partial void LogDispatcherException(Microsoft.Extensions.Logging.ILogger logger, Exception exception);

    [LoggerMessage(EventId = 101, Level = LogLevel.Critical, Message = "Unbehandelte AppDomain-Ausnahme. Terminierend: {Terminating}.")]
    private static partial void LogAppDomainException(Microsoft.Extensions.Logging.ILogger logger, Exception exception, bool terminating);

    [LoggerMessage(EventId = 102, Level = LogLevel.Error, Message = "Unbeobachtete Task-Ausnahme.")]
    private static partial void LogUnobservedTaskException(Microsoft.Extensions.Logging.ILogger logger, Exception exception);

    [LoggerMessage(EventId = 103, Level = LogLevel.Critical, Message = "Anwendung konnte nicht gestartet werden.")]
    private static partial void LogStartupFailed(Microsoft.Extensions.Logging.ILogger logger, Exception exception);

    // Ein Meldungsfenster ohne Besitzer: In einer Instanz, die sich gleich wieder beendet, gibt es
    // kein Fenster, und der Dialogdienst des Hosts steht noch nicht.
    private static void ShowStandaloneError(string title, string message) =>
        _ = MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>
    /// Wird diese Instanz die erste des Benutzers? Sonst reicht sie die Datei an die laufende weiter
    /// und beendet sich.
    /// </summary>
    /// <returns><see langword="true"/>, wenn diese Instanz weiterstartet.</returns>
    private async Task<bool> ClaimFirstInstanceAsync(string[] arguments)
    {
        InstanceStartup startup = InstanceStartup.FromCommandLine(arguments);
        _firstInstance = FirstInstance.TryClaim(startup);
        if (_firstInstance is not null)
        {
            return true;
        }

        if (await FirstInstance.HandOverAsync(startup, ShowStandaloneError).ConfigureAwait(true))
        {
            // Ohne Fenster beendet sich WPF nicht von selbst.
            Shutdown(exitCode: 0);
            return false;
        }

        // Beendet sich die laufende Instanz gerade, lauscht sie nicht mehr, und nach ihrem Ende ist
        // die Sperre frei. Dann übernimmt diese, statt den Doppelklick zu verwerfen.
        _firstInstance = FirstInstance.TryClaim(startup.WithoutRejectedFile());
        if (_firstInstance is not null)
        {
            return true;
        }

        ShowStandaloneError(OpenFileMessages.HandOverFailedTitle, OpenFileMessages.HandOverFailed(startup.File.AbsolutePath));
        Shutdown(exitCode: 1);
        return false;
    }

    private void ConnectOpenRequests(FirstInstance firstInstance)
    {
        // Beide stehen nach StartHostAndShowMainWindowAsync fest; ohne sie käme der Aufruf nicht hierher.
        ExternalFileOpener opener = _host!.Services.GetRequiredService<ExternalFileOpener>();
        ILogger<App> logger = _logger!;
        // Der Empfänger läuft auf dem Thread des Kanals; geöffnet wird auf dem UI-Thread.
        firstInstance.Requests.Attach(request => _ = Dispatcher.InvokeAsync(
            () => ObservedTask.Forget(OpenFromOutsideAsync(firstInstance, opener, request), logger)));
    }

    private async Task OpenFromOutsideAsync(FirstInstance firstInstance, ExternalFileOpener opener, MarkdownPathCheck request)
    {
        // Ohne Kanal endet das Programm gerade; ein noch wartender Auftrag kommt zu spät.
        if (!firstInstance.IsListening || MainWindow is not { } window)
        {
            return;
        }
        // Erst nach vorn, dann öffnen: Eine Rückfrage zu ungespeicherten Änderungen soll vor dem
        // Fenster erscheinen, auf das der Nutzer gerade schaut.
        WindowActivation.BringToFront(window);
        await opener.OpenAsync(request).ConfigureAwait(true);
    }

    private async Task StartHostAndShowMainWindowAsync()
    {
        IHost host = AppHostBuilder.Build();
        _host = host;
        _logger = host.Services.GetRequiredService<ILogger<App>>();
        _shutdown = new HostShutdown(() => host.StopAsync(ServiceStopTimeout), host.Services.GetRequiredService<ILogger<HostShutdown>>());

        SplashViewModel splash = ShowSplash(host.Services);
        CancellationToken stoppingToken = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;

        // Die Schema-Migration läuft vor StartAsync, weil die Hintergrunddienste
        // (ParseOrchestrator, Fts5IndexMaintainer) sofort beim Start lesen. Auf einer frischen
        // Datenbank träfen sie sonst auf fehlende Tabellen.
        splash.StatusText = UiText.Get("Startup_Status_PreparingDatabase");
        AppInitializer initializer = host.Services.GetRequiredService<AppInitializer>();
        await initializer.InitializeAsync(AppInitializer.DefaultMinimumDisplayDuration, stoppingToken).ConfigureAwait(true);

        await host.StartAsync(stoppingToken).ConfigureAwait(true);

        new ThemeFollower(
            this,
            host.Services.GetRequiredService<ISettingsService>(),
            host.Services.GetRequiredService<ThemeApplier>(),
            host.Services.GetRequiredService<PreviewViewModel>(),
            host.Services.GetRequiredService<IEffectiveThemeProvider>()).Start();

        ShowMainWindow(host.Services);
    }

    private SplashViewModel ShowSplash(IServiceProvider services)
    {
        SplashViewModel splash = services.GetRequiredService<SplashViewModel>();
        _splashWindow = services.GetRequiredService<SplashWindow>();
        _splashWindow.DataContext = splash;
        _splashWindow.Show();
        return splash;
    }

    private void ShowMainWindow(IServiceProvider services)
    {
        MainWindow mainWindow = services.GetRequiredService<MainWindow>();
        // Nach den Handlern des Fensters selbst: Hält dessen Rückfrage zu ungespeicherten
        // Änderungen das Schließen an, beginnt das Stoppen gar nicht erst.
        mainWindow.Closing += OnMainWindowClosing;
        MainWindow = mainWindow;
        mainWindow.Show();

        _splashWindow?.Close();
        _splashWindow = null;
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs args)
    {
        if (_shutdown is not null && _shutdown.HoldClosing(args))
        {
            // Ein Close aus dem laufenden Closing heraus lehnt WPF ab; deshalb danach.
            _ = Dispatcher.InvokeAsync(StopServicesAndCloseAsync);
        }
    }

    private async Task StopServicesAndCloseAsync()
    {
        if (_shutdown is null)
        {
            return;
        }
        if (_firstInstance is not null)
        {
            await _firstInstance.StopListeningAsync().ConfigureAwait(true);
        }
        await _shutdown.StopAsync().ConfigureAwait(true);
        MainWindow?.Close();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        args.Handled = true;

        UnhandledExceptionAction action = _exceptionPolicy.Decide(args.Exception);
        if (action == UnhandledExceptionAction.AbortStartup)
        {
            AbortStartup(args.Exception);
            return;
        }

        LogDispatcherFailure(args.Exception);
        switch (action)
        {
            case UnhandledExceptionAction.ShowDialog:
                _ = MessageBox.Show(
                    ErrorMessages.Unhandled(args.Exception, isTerminating: false),
                    ErrorMessages.UnhandledTitle(isTerminating: false),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                _exceptionPolicy.DialogClosed();
                break;
            case UnhandledExceptionAction.AbortStartup:
            case UnhandledExceptionAction.LogOnly:
                break;
        }
    }

    private void AbortStartup(Exception exception)
    {
        // Ist schon der Host gescheitert, gibt es seinen Logger noch nicht.
        if (_logger is not null)
        {
            LogStartupFailed(_logger, exception);
        }
        else
        {
            Log.Fatal(exception, "Anwendung konnte nicht gestartet werden.");
        }

        // Ausblenden, nicht schließen: WPF hat das Startbild als erstes Fenster zum Hauptfenster
        // gemacht. Sein Schließen beendete die Anwendung sofort mit Code 0, und der Fehlercode
        // unten ginge verloren. Shutdown schließt es danach mit.
        _splashWindow?.Hide();
        _splashWindow = null;

        _ = MessageBox.Show(
            ErrorMessages.StartupFailure(exception),
            UiText.Get("Startup_Failure_Title"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        Shutdown(exitCode: 1);
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
        else
        {
            Log.Fatal(exception, "Unbehandelte AppDomain-Ausnahme. Terminierend: {Terminating}.", args.IsTerminating);
        }

        if (args.IsTerminating)
        {
            // Direkt auf dem Thread, der den Fehler meldet, nicht über den Dispatcher: Ein
            // blockierter UI-Thread hielte ein synchrones Invoke fest, und der Prozess hinge,
            // statt zu enden. Ein Meldungsfenster ohne Besitzer darf jeder Thread zeigen.
            _ = MessageBox.Show(
                ErrorMessages.Unhandled(exception, isTerminating: true),
                ErrorMessages.UnhandledTitle(isTerminating: true),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (_logger is not null)
        {
            LogUnobservedTaskException(_logger, args.Exception);
        }
        else
        {
            Log.Error(args.Exception, "Unbeobachtete Task-Ausnahme.");
        }
        args.SetObserved();
    }

    private void LogDispatcherFailure(Exception exception)
    {
        // Der Host-Logger schreibt über Serilog in dieselben Senken wie der statische Log. Beide
        // zugleich ergäben jeden Eintrag doppelt; der statische springt nur ein, solange der Host
        // noch nicht steht.
        if (_logger is not null)
        {
            LogDispatcherException(_logger, exception);
        }
        else
        {
            Log.Error(exception, "Unbehandelte Ausnahme im UI-Thread.");
        }
    }
}
