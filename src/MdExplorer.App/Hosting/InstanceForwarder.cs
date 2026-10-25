using System.IO;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Hosting;

/// <summary>
/// Reicht den Auftrag einer zweiten Instanz an die laufende weiter, höchstens bis zur
/// <see cref="Timeout"/>.
/// </summary>
/// <remarks>
/// Die Zeitgrenze deckt drei Fälle ab, die von außen gleich aussehen: Die laufende Instanz hängt,
/// ihr Kanal ist mit einer anderen Übergabe belegt, oder sie beendet sich gerade und lauscht nicht
/// mehr. In keinem davon darf die zweite Instanz ewig warten oder still verschwinden.
/// </remarks>
internal sealed partial class InstanceForwarder
{
    private readonly IInstanceChannelClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InstanceForwarder> _logger;

    /// <summary>Erzeugt die Weiterleitung.</summary>
    /// <param name="client">Der Kanal zur laufenden Instanz.</param>
    /// <param name="timeProvider">Uhr für die Zeitgrenze.</param>
    /// <param name="logger">Protokoll.</param>
    public InstanceForwarder(IInstanceChannelClient client, TimeProvider timeProvider, ILogger<InstanceForwarder> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Längste Wartezeit. Eine gesunde Instanz antwortet in Millisekunden; fünf Sekunden lassen
    /// einem stark ausgelasteten Rechner Luft, ohne dass der Nutzer den Doppelklick für verloren hält.
    /// </summary>
    public static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(5);

    /// <summary>Übergibt den Auftrag.</summary>
    /// <param name="absolutePath">Die Datei; <see langword="null"/>, wenn nur das Fenster nach vorn soll.</param>
    /// <returns><see langword="true"/>, wenn die laufende Instanz den Auftrag bestätigt hat.</returns>
    public async Task<bool> ForwardAsync(string? absolutePath)
    {
        using CancellationTokenSource timeout = new(Timeout, _timeProvider);
        try
        {
            await _client.SendAsync(absolutePath, timeout.Token).ConfigureAwait(true);
            LogForwarded(_logger, absolutePath);
            return true;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            LogTimedOut(_logger, Timeout.TotalSeconds);
            return false;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            LogFailed(_logger, exception);
            return false;
        }
    }

    [LoggerMessage(EventId = 1710, Level = LogLevel.Information, Message = "An die laufende Instanz übergeben: {Path}")]
    private static partial void LogForwarded(ILogger logger, string? path);

    [LoggerMessage(EventId = 1711, Level = LogLevel.Warning, Message = "Die laufende Instanz hat nicht binnen {Seconds} s geantwortet.")]
    private static partial void LogTimedOut(ILogger logger, double seconds);

    [LoggerMessage(EventId = 1712, Level = LogLevel.Warning, Message = "Übergabe an die laufende Instanz gescheitert.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
