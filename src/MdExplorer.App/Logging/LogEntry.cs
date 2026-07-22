using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Logging;

/// <summary>
/// In-Memory-Repräsentation eines Log-Events für den integrierten Log-Viewer.
/// Bewusst ohne Verweis auf Serilog-Typen, damit die UI- und Test-Schicht
/// nicht an die Sink-Implementierung gekoppelt sind.
/// </summary>
/// <param name="Timestamp">Erfassungszeitpunkt mit Offset.</param>
/// <param name="Level">Normalisiertes <see cref="LogLevel"/> (Mapping aus Serilog).</param>
/// <param name="SourceContext">Kategorie-Name aus <c>ILogger&lt;T&gt;</c> oder leer.</param>
/// <param name="Message">Vollständig gerenderte Nachricht mit aufgelösten Properties.</param>
/// <param name="Exception">Stringifizierter Stack-Trace oder <see langword="null"/>.</param>
internal sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string SourceContext,
    string Message,
    string? Exception);
