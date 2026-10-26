using System.IO;
using System.IO.Pipes;
using System.Threading.Channels;
using MdExplorer.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Hosting;

/// <summary>
/// Nimmt in der ersten Instanz die Aufträge späterer Instanzen entgegen.
/// </summary>
/// <remarks>
/// <para>
/// Eine Übergabe nach der anderen: Der Kanal hat genau eine Server-Instanz. Eine weitere Übergabe
/// wartet beim Verbinden, bis die laufende fertig ist. Das dauert Millisekunden, weil hier nur
/// gelesen, vorgemerkt und bestätigt wird. Was mit dem Pfad geschieht, erledigt der Empfänger
/// danach auf einem eigenen Ablauf, einer nach dem anderen in der Reihenfolge des Eingangs. Prüft
/// er dabei eine Datei auf einem Netzlaufwerk, das nicht antwortet, wartet darauf keine andere
/// Instanz.
/// </para>
/// <para>
/// Ein Client, der verbindet und dann schweigt, belegte den Kanal für alle anderen. Deshalb gilt
/// je Verbindung <see cref="ConnectionTimeout"/>.
/// </para>
/// </remarks>
internal sealed partial class NamedPipeInstanceServer : IAsyncDisposable
{
    /// <summary>So oft wird das Anlegen des Kanals versucht, bevor die Übergabe ausfällt.</summary>
    private const int MaxCreateAttempts = 5;

    private readonly string _pipeName;
    private readonly Action<string?> _onRequest;
    private readonly ILogger<NamedPipeInstanceServer> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Channel<string?> _requests = Channel.CreateUnbounded<string?>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private Task _listening = Task.CompletedTask;

    /// <summary>Erzeugt den Server; er lauscht erst nach <see cref="Start"/>.</summary>
    /// <param name="pipeName">Name des Kanals.</param>
    /// <param name="onRequest">
    /// Empfänger eines Auftrags. Aufgerufen auf einem Thread des Pools, nach der Bestätigung und
    /// einer nach dem anderen; er darf also dauern.
    /// </param>
    /// <param name="logger">Protokoll.</param>
    public NamedPipeInstanceServer(string pipeName, Action<string?> onRequest, ILogger<NamedPipeInstanceServer> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(onRequest);
        ArgumentNullException.ThrowIfNull(logger);

        _pipeName = pipeName;
        _onRequest = onRequest;
        _logger = logger;
    }

    /// <summary>Längste Dauer einer einzelnen Übergabe, vom Verbinden bis zur Bestätigung.</summary>
    public static TimeSpan ConnectionTimeout { get; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Abstand zwischen zwei Versuchen, den Kanal anzulegen. Nach einer Übergabe kann das Anlegen
    /// kurz scheitern, solange Windows die vorige Instanz noch nicht freigegeben hat.
    /// </summary>
    public static TimeSpan CreateRetryDelay { get; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Beginnt zu lauschen und die Aufträge abzuarbeiten.</summary>
    /// <remarks>
    /// Ohne Abbruch an <see cref="Task.Run(Func{Task}, CancellationToken)"/>: Das Lauschen soll immer
    /// anlaufen und selbst auf das Ende reagieren. Mit Abbruch endete ein früh gestoppter Server als
    /// abgebrochene Aufgabe, und <see cref="DisposeAsync"/> würfe beim Warten darauf.
    /// </remarks>
    public void Start()
    {
        _listening = Task.Run(() => ListenAsync(_stopping.Token), CancellationToken.None);
        _ = Task.Run(HandleRequestsAsync, CancellationToken.None);
    }

    /// <summary>
    /// Hört auf zu lauschen und wartet, bis die laufende Übergabe beendet ist. Auf einen Auftrag,
    /// der noch abgearbeitet wird, wartet es nicht; er endet mit dem Prozess.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_stopping.IsCancellationRequested)
        {
            return;
        }
        await _stopping.CancelAsync().ConfigureAwait(false);
        await _listening.ConfigureAwait(false);
        _ = _requests.Writer.TryComplete();
        _stopping.Dispose();
    }

    [LoggerMessage(EventId = 1720, Level = LogLevel.Information, Message = "Kanal {PipeName} nimmt Aufträge anderer Instanzen an.")]
    private static partial void LogListening(ILogger logger, string pipeName);

    [LoggerMessage(EventId = 1721, Level = LogLevel.Error, Message = "Kanal {PipeName} lässt sich nicht anlegen; Doppelklicks bei laufendem Programm kommen nicht an.")]
    private static partial void LogCannotListen(ILogger logger, string pipeName, Exception exception);

    [LoggerMessage(EventId = 1722, Level = LogLevel.Warning, Message = "Übergabe einer anderen Instanz verworfen.")]
    private static partial void LogConnectionFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1723, Level = LogLevel.Warning, Message = "Übergabe einer anderen Instanz nach {Seconds} s abgebrochen.")]
    private static partial void LogConnectionTimedOut(ILogger logger, double seconds);

    [LoggerMessage(EventId = 1724, Level = LogLevel.Warning, Message = "Kanal {PipeName} ließ sich nicht anlegen (Versuch {Attempt} von {MaxAttempts}); neuer Versuch.")]
    private static partial void LogCreateRetry(ILogger logger, string pipeName, int attempt, int maxAttempts, Exception exception);

    [LoggerMessage(EventId = 1725, Level = LogLevel.Error, Message = "Auftrag einer anderen Instanz ließ sich nicht verarbeiten.")]
    private static partial void LogRequestFailed(ILogger logger, Exception exception);

    /// <returns><see langword="false"/>, wenn das Programm währenddessen endet.</returns>
    private static async Task<bool> WaitBeforeRetryAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(CreateRetryDelay, stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        LogListening(_logger, _pipeName);
        int failedAttempts = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Je Übergabe eine frische Server-Instanz: Nach dem Trennen ist die alte nicht
                // mehr verbindbar.
                if (!await AcceptOneAsync(stoppingToken).ConfigureAwait(false))
                {
                    return;
                }
                failedAttempts = 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failedAttempts++;
                if (failedAttempts >= MaxCreateAttempts)
                {
                    // Der Name ist dauerhaft vergeben, etwa von einem anderen Prozess, der ihn
                    // besetzt hält. Das Programm läuft weiter; nur die Übergabe von außen fällt
                    // aus, und das steht im Protokoll.
                    LogCannotListen(_logger, _pipeName, exception);
                    return;
                }
                LogCreateRetry(_logger, _pipeName, failedAttempts, MaxCreateAttempts, exception);
                if (!await WaitBeforeRetryAsync(stoppingToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
    }

    /// <returns><see langword="false"/>, wenn nicht weiter gelauscht wird.</returns>
    /// <exception cref="IOException">Der Kanal lässt sich nicht anlegen.</exception>
    /// <exception cref="UnauthorizedAccessException">Der Kanal gehört einem anderen Benutzer.</exception>
    private async Task<bool> AcceptOneAsync(CancellationToken stoppingToken)
    {
        NamedPipeServerStream pipe = new(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (IOException exception)
            {
                // Der Client hat sich vor dem Verbinden wieder getrennt; die nächste Übergabe
                // bekommt eine frische Server-Instanz.
                LogConnectionFailed(_logger, exception);
                return true;
            }
            await ServeAsync(pipe, stoppingToken).ConfigureAwait(false);
        }
        return !stoppingToken.IsCancellationRequested;
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        using CancellationTokenSource connection = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        connection.CancelAfter(ConnectionTimeout);
        try
        {
            string? absolutePath = await InstanceMessageCodec.ReadRequestAsync(pipe, connection.Token).ConfigureAwait(false);
            // Erst vormerken, dann bestätigen: Was die andere Instanz als angenommen meldet, ist
            // auch angenommen. Verarbeitet wird es in HandleRequestsAsync, nicht hier.
            _ = _requests.Writer.TryWrite(absolutePath);
            await InstanceMessageCodec.WriteAcknowledgementAsync(pipe, connection.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            LogConnectionTimedOut(_logger, ConnectionTimeout.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            // Das Programm endet; die Übergabe meldet der Client selbst als gescheitert.
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            LogConnectionFailed(_logger, exception);
        }
    }

    private async Task HandleRequestsAsync()
    {
        // Ohne Abbruch: Die Schleife endet, wenn DisposeAsync den Kanal schließt; was bis dahin
        // vorgemerkt ist, wird noch abgearbeitet.
        await foreach (string? absolutePath in _requests.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            try
            {
                _onRequest(absolutePath);
            }
            catch (Exception exception) when (BackgroundServiceWatchdog.IsRecoverable(exception, CancellationToken.None))
            {
                // Ein einzelner Auftrag darf die Abarbeitung der übrigen nicht beenden.
                LogRequestFailed(_logger, exception);
            }
        }
    }
}
