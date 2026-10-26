using System.Diagnostics.CodeAnalysis;
using MdExplorer.App.Services;
using MdExplorer.Core.FileSystem;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;

namespace MdExplorer.App.Hosting;

/// <summary>
/// Die erste Instanz des Benutzers: Sie hält die Sperre und nimmt über den Kanal Dateien späterer
/// Instanzen an. Eine spätere Instanz wird gar nicht erst zur <see cref="FirstInstance"/>, sondern
/// reicht ihre Datei mit <see cref="HandOverAsync"/> weiter.
/// </summary>
/// <remarks>
/// Entschieden wird vor dem Host: Eine zweite Instanz darf weder die Datenbank öffnen noch
/// Hintergrunddienste starten. Deshalb protokolliert die Klasse über den statischen Serilog-Logger,
/// der in dieselben Senken schreibt wie später der Host. Die Teile mit eigener Logik
/// (<see cref="MarkdownPathValidator"/>, <see cref="InstanceForwarder"/>,
/// <see cref="NamedPipeInstanceServer"/>, <see cref="PendingOpenRequests"/>) sind einzeln geprüft;
/// hier werden sie nur gegen Mutex, Kanal und Meldungsfenster verbunden.
/// </remarks>
[ExcludeFromCodeCoverage]
internal sealed class FirstInstance : IDisposable
{
    private readonly SerilogLoggerFactory _loggers;
    private readonly InstanceLock _lock;
    private NamedPipeInstanceServer? _server;

    private FirstInstance(SerilogLoggerFactory loggers, InstanceLock instanceLock, NamedPipeInstanceServer server, PendingOpenRequests requests)
    {
        _loggers = loggers;
        _lock = instanceLock;
        _server = server;
        Requests = requests;
    }

    /// <summary>Die Dateien, die geöffnet werden sollen: aus der Befehlszeile und von späteren Instanzen.</summary>
    public PendingOpenRequests Requests { get; }

    /// <summary>Wahr, solange der Kanal Aufträge annimmt.</summary>
    public bool IsListening => _server is not null;

    /// <summary>Wird diese Instanz die erste des Benutzers?</summary>
    /// <param name="startup">Was der Start mitbringt.</param>
    /// <returns>Die erste Instanz, oder <see langword="null"/>, wenn schon eine läuft.</returns>
    public static FirstInstance? TryClaim(InstanceStartup startup)
    {
        ArgumentNullException.ThrowIfNull(startup);

        SerilogLoggerFactory loggers = new(Serilog.Log.Logger);
        InstanceLock instanceLock = InstanceLock.Acquire(startup.Identity.LockName, loggers.CreateLogger<InstanceLock>());
        if (!instanceLock.IsFirstInstance)
        {
            instanceLock.Dispose();
            loggers.Dispose();
            return null;
        }

        PendingOpenRequests requests = new();
        requests.Post(startup.File);
        MarkdownPathValidator validator = startup.Validator;
        // Der Kanal lauscht schon während des Startbilds; was dann ankommt, halten die
        // PendingOpenRequests bis zum Hauptfenster zurück.
        NamedPipeInstanceServer server = new(
            startup.Identity.PipeName,
            path => requests.Post(validator.CheckForwarded(path)),
            loggers.CreateLogger<NamedPipeInstanceServer>());
        server.Start();
        return new FirstInstance(loggers, instanceLock, server, requests);
    }

    /// <summary>Reicht die Datei des Starts an die laufende Instanz weiter.</summary>
    /// <param name="startup">Was der Start mitbringt.</param>
    /// <param name="showError">Zeigt eine Meldung; in einer Instanz, die gleich endet, ohne Fenster.</param>
    /// <returns><see langword="true"/>, wenn die laufende Instanz übernommen hat.</returns>
    public static async Task<bool> HandOverAsync(InstanceStartup startup, Action<string, string> showError)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(showError);

        if (startup.File.Problem is not null)
        {
            // Die laufende Instanz kennt das Arbeitsverzeichnis dieser nicht und könnte einen
            // relativen Pfad nicht deuten; deshalb meldet diese selbst und holt nur das Fenster.
            showError(OpenFileMessages.RejectedTitle, OpenFileMessages.Rejected(startup.File));
        }

        using SerilogLoggerFactory loggers = new(Serilog.Log.Logger);
        InstanceForwarder forwarder = new(
            new NamedPipeInstanceClient(startup.Identity.PipeName),
            TimeProvider.System,
            loggers.CreateLogger<InstanceForwarder>());
        WindowActivation.AllowRunningInstanceToComeForward();
        return await forwarder.ForwardAsync(startup.File.AbsolutePath).ConfigureAwait(true);
    }

    /// <summary>
    /// Nimmt keine Aufträge mehr an. Vor dem Stoppen der Dienste aufrufen: Ein Doppelklick
    /// während des Beendens soll nicht in einem Fenster landen, das gleich schließt.
    /// </summary>
    public async Task StopListeningAsync()
    {
        if (_server is not { } server)
        {
            return;
        }
        _server = null;
        await server.DisposeAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Gibt die Sperre frei. Erst nach dem Schließen der Datenbank aufrufen: Bis dahin soll keine
    /// neue Instanz sie öffnen. Ein Kanal, der dann noch lauscht, endet mit dem Prozess.
    /// </summary>
    public void Dispose()
    {
        _lock.Dispose();
        _loggers.Dispose();
    }
}

/// <summary>Was ein Start mitbringt: wer der Benutzer ist und welche Datei geöffnet werden soll.</summary>
/// <param name="Identity">Namen von Sperre und Kanal.</param>
/// <param name="Validator">Prüft Pfade, auch die später weitergereichten.</param>
/// <param name="File">Die geprüfte Datei aus der Befehlszeile.</param>
[ExcludeFromCodeCoverage]
internal sealed record InstanceStartup(InstanceIdentity Identity, MarkdownPathValidator Validator, MarkdownPathCheck File)
{
    /// <summary>Liest den Start aus der Befehlszeile.</summary>
    /// <param name="arguments">Die Befehlszeile.</param>
    public static InstanceStartup FromCommandLine(IReadOnlyList<string> arguments)
    {
        MarkdownPathValidator validator = new(new LocalFileSystem());
        return new InstanceStartup(
            InstanceIdentity.ForCurrentUser(),
            validator,
            validator.CheckCommandLine(arguments, Environment.CurrentDirectory));
    }

    /// <summary>
    /// Derselbe Start ohne abgewiesene Datei: Die Meldung dazu hat <see cref="FirstInstance.HandOverAsync"/>
    /// schon gezeigt, sie soll nicht ein zweites Mal kommen.
    /// </summary>
    public InstanceStartup WithoutRejectedFile() => File.Problem is null ? this : this with { File = MarkdownPathCheck.None };
}
