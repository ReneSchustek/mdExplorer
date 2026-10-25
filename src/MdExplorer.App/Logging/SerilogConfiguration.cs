using System.Globalization;
using System.IO;
using MdExplorer.Core;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

using System.Diagnostics.CodeAnalysis;

namespace MdExplorer.App.Logging;

/// <summary>
/// Konfiguriert den Serilog-Logger für die Anwendung.
/// Eine Datei je Tag, die beim Erreichen der Größengrenze auf eine Folgedatei umrollt; zusätzlich
/// die Debug-Ausgabe, die nur unter einem angehängten Debugger sichtbar ist, und der Puffer für den
/// Log-Viewer.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class SerilogConfiguration
{
    private const string FileTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    // Zusammen mit dem Limit von 14 Dateien bleibt das Protokollverzeichnis unter 700 MB, auch
    // wenn eine Fehlerschleife schreibt. Weil auch Folgedateien zählen, reichen 14 Dateien bei
    // großem Aufkommen für weniger als 14 Tage.
    private const long FileSizeLimitBytes = 50L * 1024 * 1024;

    /// <summary>
    /// Baut einen <see cref="Serilog.ILogger"/> für den globalen Static-Logger sowie
    /// für die Verwendung in <c>UseSerilog</c>.
    /// </summary>
    /// <remarks>
    /// Das Protokoll entsteht vor dem Host und liest deshalb seine Konfiguration selbst, aus
    /// denselben Quellen wie der Host (<see cref="Hosting.AppHostBuilder.Settings"/>):
    /// <c>appsettings.json</c> neben der Programmdatei, dann Umgebungsvariablen.
    /// </remarks>
    public static Serilog.ILogger BuildLogger()
    {
        string logFile = Path.Combine(AppPaths.Default.LogsDirectory, "app-.log");
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        return new LoggerConfiguration()
            .MinimumLevel.Is(LogLevelSetting.Read(configuration))
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
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true)
            .WriteTo.Sink(MemorySink.Instance)
            .CreateLogger();
    }
}
