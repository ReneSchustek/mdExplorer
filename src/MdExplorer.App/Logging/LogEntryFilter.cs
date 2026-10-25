using MdExplorer.App.Localization;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Logging;

/// <summary>Auswahleintrag des Stufenfilters.</summary>
/// <param name="MinimumLevel">Untere Grenze: Einträge mit niedrigerem Level werden ausgeblendet.</param>
/// <param name="Display">Anzeige-Label.</param>
internal sealed record LogLevelFilter(LogLevel MinimumLevel, string Display);

/// <summary>Auswahleintrag des Zeitraumfilters.</summary>
/// <param name="Window">Wie weit zurück; <see langword="null"/> für alles.</param>
/// <param name="Display">Anzeige-Label.</param>
internal sealed record LogPeriodFilter(TimeSpan? Window, string Display);

/// <summary>
/// Welche Einträge das Protokollfenster zeigt: ab einer Stufe, mit einem Suchtext im Text oder in
/// der Quelle, und aus einem Zeitraum bis jetzt.
/// </summary>
internal static class LogEntryFilter
{
    /// <summary>Die wählbaren Stufen, von „alles“ bis „nur kritisch“.</summary>
    public static IReadOnlyList<LogLevelFilter> Levels { get; } =
    [
        new(LogLevel.Trace, UiText.Get("LogViewer_Level_All")),
        new(LogLevel.Debug, UiText.Get("LogViewer_Level_Debug")),
        new(LogLevel.Information, UiText.Get("LogViewer_Level_Information")),
        new(LogLevel.Warning, UiText.Get("LogViewer_Level_Warning")),
        new(LogLevel.Error, UiText.Get("LogViewer_Level_Error")),
        new(LogLevel.Critical, UiText.Get("LogViewer_Level_Critical")),
    ];

    /// <summary>Die wählbaren Zeiträume.</summary>
    public static IReadOnlyList<LogPeriodFilter> Periods { get; } =
    [
        new(null, UiText.Get("LogViewer_Period_All")),
        new(TimeSpan.FromMinutes(15), UiText.Get("LogViewer_Period_FifteenMinutes")),
        new(TimeSpan.FromHours(1), UiText.Get("LogViewer_Period_Hour")),
        new(TimeSpan.FromDays(1), UiText.Get("LogViewer_Period_Day")),
    ];

    /// <summary>Ob <paramref name="entry"/> zu sehen ist.</summary>
    /// <param name="entry">Der Eintrag.</param>
    /// <param name="level">Gewählte Stufe.</param>
    /// <param name="query">Suchtext; leer zeigt alles.</param>
    /// <param name="period">Gewählter Zeitraum.</param>
    /// <param name="now">Jetzt, Ende des Zeitraums.</param>
    public static bool Matches(LogEntry entry, LogLevelFilter level, string query, LogPeriodFilter period, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(period);

        if (entry.Level < level.MinimumLevel)
        {
            return false;
        }
        if (period.Window is { } window && entry.Timestamp < now - window)
        {
            return false;
        }
        return string.IsNullOrWhiteSpace(query)
            || entry.Message.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.SourceContext.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
