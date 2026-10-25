using System.IO;
using System.IO.Pipes;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Hosting;

/// <summary>
/// Nimmt in der ersten Instanz die Aufträge späterer Instanzen entgegen.
/// </summary>
/// <remarks>
/// <para>
/// Eine Übergabe nach der anderen: Der Kanal hat genau eine Server-Instanz. Eine weitere Übergabe
/// wartet beim Verbinden, bis die laufende fertig ist; das dauert Millisekunden, weil hier nur
/// gelesen und bestätigt wird. Was mit dem Pfad geschieht, entscheidet der Empfänger auf seinem
/// eigenen Thread.
/// </para>
/// <para>
/// Ein Client, der verbindet und dann schweigt, belegte den Kanal für alle anderen. Deshalb gilt
/// je Verbindung <see cref="ConnectionTimeout"/>.
/// </para>
/// </remarks>
internal sealed partial class NamedPipeInstanceServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly Action<string?> _onRequest;
    private readonly ILogger<NamedPipeInstanceServer> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private Task _listening = Task.CompletedTask;

    /// <summary>Erzeugt den Server; er lauscht erst nach <see cref="Start"/>.</summary>
    /// <param name="pipeName">Name des Kanals.</param>
    /// <param name="onRequest">Empfänger eines Auftrags, aufgerufen auf einem Thread des Pools.</param>
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

    /// <summary>Beginnt zu lauschen.</summary>
    /// <remarks>
    /// Ohne Abbruch an <see cref="Task.Run(Func{Task}, CancellationToken)"/>: Das Lauschen soll immer
    /// anlaufen und selbst auf das Ende reagieren. Mit Abbruch endete ein früh gestoppter Server als
    /// abgebrochene Aufgabe, und <see cref="DisposeAsync"/> würfe beim Warten darauf.
    /// </remarks>
    public void Start() => _listening = Task.Run(() => ListenAsync(_stopping.Token), CancellationToken.None);

    /// <summary>Hört auf zu lauschen und wartet, bis die laufende Übergabe beendet ist.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_stopping.IsCancellationRequested)
        {
            return;
        }
        await _stopping.CancelAsync().ConfigureAwait(false);
        await _listening.ConfigureAwait(false);
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

    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        LogListening(_logger, _pipeName);
        try
        {
            while (await AcceptOneAsync(stoppingToken).ConfigureAwait(false))
            {
                // Je Übergabe eine frische Server-Instanz: Nach dem Trennen ist die alte nicht
                // mehr verbindbar.
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Der Name ist schon vergeben, etwa von einem anderen Prozess, der ihn besetzt hält.
            // Das Programm läuft weiter; nur die Übergabe von außen fällt aus, und das steht im
            // Protokoll.
            LogCannotListen(_logger, _pipeName, exception);
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
            _onRequest(absolutePath);
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
}
