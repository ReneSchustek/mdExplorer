using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Services;

/// <summary>
/// Führt eine geprüfte Anfrage von außen aus: Eine gültige Datei geht in den Dokumentbereich,
/// ein abgewiesener Pfad wird gemeldet.
/// </summary>
/// <remarks>
/// Geöffnet wird über denselben Weg wie ein Klick in der Dateiliste. Damit greift auch dieselbe
/// Rückfrage zu ungespeicherten Änderungen, und eine Datei außerhalb des Index wird direkt von
/// der Platte gelesen.
/// </remarks>
internal sealed partial class ExternalFileOpener
{
    private readonly Func<string, Task<bool>> _openFile;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ExternalFileOpener> _logger;

    /// <summary>Erzeugt den Öffner.</summary>
    /// <param name="openFile">Öffnet eine Datei über ihren Pfad; <see langword="false"/>, wenn der Nutzer abgebrochen hat.</param>
    /// <param name="dialogs">Zeigt die Meldung zu einem abgewiesenen Pfad.</param>
    /// <param name="logger">Protokoll.</param>
    public ExternalFileOpener(Func<string, Task<bool>> openFile, IDialogService dialogs, ILogger<ExternalFileOpener> logger)
    {
        ArgumentNullException.ThrowIfNull(openFile);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(logger);

        _openFile = openFile;
        _dialogs = dialogs;
        _logger = logger;
    }

    /// <summary>Öffnet die Datei oder meldet, warum nicht.</summary>
    /// <param name="request">Die geprüfte Anfrage.</param>
    public async Task OpenAsync(MarkdownPathCheck request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Problem is { } problem)
        {
            LogRejected(_logger, request.RejectedValue, problem);
            _dialogs.ShowError(OpenFileMessages.RejectedTitle, OpenFileMessages.Rejected(request));
            return;
        }
        if (request.AbsolutePath is not { } path)
        {
            return;
        }

        LogOpening(_logger, path);
        if (!await _openFile(path).ConfigureAwait(true))
        {
            // Der Nutzer hat die Rückfrage zu ungespeicherten Änderungen abgebrochen; das
            // bisherige Dokument bleibt offen.
            LogKeptCurrentDocument(_logger, path);
        }
    }

    [LoggerMessage(EventId = 1700, Level = LogLevel.Warning, Message = "Datei von außen abgewiesen ({Problem}): {Path}")]
    private static partial void LogRejected(ILogger logger, string? path, MarkdownPathProblem problem);

    [LoggerMessage(EventId = 1701, Level = LogLevel.Information, Message = "Datei von außen wird geöffnet: {Path}")]
    private static partial void LogOpening(ILogger logger, string path);

    [LoggerMessage(EventId = 1702, Level = LogLevel.Information, Message = "Datei {Path} nicht geöffnet; ungespeicherte Änderungen bleiben im Editor.")]
    private static partial void LogKeptCurrentDocument(ILogger logger, string path);
}
