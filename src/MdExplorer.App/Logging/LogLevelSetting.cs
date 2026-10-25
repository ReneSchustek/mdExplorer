using Microsoft.Extensions.Configuration;
using Serilog.Events;

namespace MdExplorer.App.Logging;

/// <summary>
/// Die Protokollstufe aus der Konfiguration, Schlüssel <c>Serilog:MinimumLevel</c>. Zur Diagnose
/// lässt sie sich so anheben, etwa mit der Umgebungsvariable <c>Serilog__MinimumLevel=Debug</c>.
/// </summary>
internal static class LogLevelSetting
{
    /// <summary>Schlüssel in der Konfiguration.</summary>
    public const string Key = "Serilog:MinimumLevel";

    /// <summary>Was ohne oder mit unlesbarer Angabe gilt.</summary>
    public const LogEventLevel Default = LogEventLevel.Information;

    /// <summary>Liest die Stufe; eine fehlende oder unbekannte Angabe ergibt <see cref="Default"/>.</summary>
    /// <param name="configuration">Konfiguration der Anwendung.</param>
    public static LogEventLevel Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Zahlen lehnt die Prüfung ab: „7" ergäbe sonst eine Stufe, die es nicht gibt.
        string? configured = configuration[Key];
        return !int.TryParse(configured, out _) && Enum.TryParse(configured, ignoreCase: true, out LogEventLevel level)
            ? level
            : Default;
    }
}
