using System.Globalization;
using System.IO;
using MdExplorer.Core;
using Serilog;
using Serilog.Events;

namespace MdExplorer.App.Logging;

/// <summary>
/// Konfiguriert den Serilog-Logger für die Anwendung.
/// File-Sink mit täglicher Rotation, 14 Tage Retention, Debug-Sink im Entwicklungsmodus.
/// </summary>
internal static class SerilogConfiguration
{
    private const string FileTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Baut einen <see cref="Serilog.ILogger"/> für den globalen Static-Logger sowie
    /// für die Verwendung in <c>UseSerilog</c>.
    /// </summary>
    public static Serilog.ILogger BuildLogger()
    {
        string logFile = Path.Combine(AppPaths.GetLogsDirectory(), "app-.log");

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "MdExplorer")
            .WriteTo.Debug(outputTemplate: FileTemplate, formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(
                path: logFile,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: FileTemplate,
                formatProvider: CultureInfo.InvariantCulture,
                shared: true,
                fileSizeLimitBytes: 50 * 1024 * 1024,
                rollOnFileSizeLimit: true)
            .WriteTo.Sink(MemorySink.Instance)
            .CreateLogger();
    }
}
