using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Shell;
using MdExplorer.App.Localization;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Services;

/// <summary>
/// Führt die Sprungliste am Taskleistensymbol aus dem Verlauf der Anwendung.
/// </summary>
/// <remarks>
/// Die Einträge sind eigene Aufgaben, die <c>MdExplorer.exe</c> mit dem Pfad starten, nicht die
/// Liste „Zuletzt verwendet“, die Windows selbst führt. So zeigt die Sprungliste genau den Verlauf
/// der Anwendung, und „Verlauf leeren“ leert beide. Ein Eintrag startet eine zweite Instanz; die
/// reicht die Datei an die laufende weiter.
/// </remarks>
[ExcludeFromCodeCoverage]
internal sealed partial class WindowsRecentFilesJumpList : IRecentFilesJumpList
{
    private readonly ILogger<WindowsRecentFilesJumpList> _logger;

    /// <summary>Erzeugt die Sprungliste.</summary>
    /// <param name="logger">Protokoll, falls Windows die Liste nicht annimmt.</param>
    public WindowsRecentFilesJumpList(ILogger<WindowsRecentFilesJumpList> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public void Show(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        string? program = Environment.ProcessPath;
        if (program is null || Application.Current is not { } application)
        {
            return;
        }

        JumpList jumpList = new() { ShowRecentCategory = false, ShowFrequentCategory = false };
        string category = UiText.Get("RecentFiles_JumpListCategory");
        foreach (string path in paths)
        {
            jumpList.JumpItems.Add(new JumpTask
            {
                Title = Path.GetFileName(path),
                Description = path,
                ApplicationPath = program,
                IconResourcePath = program,
                // Ein Dateiname unter Windows enthält kein Anführungszeichen; das Umschließen genügt.
                Arguments = "\"" + path + "\"",
                CustomCategory = category,
            });
        }

        try
        {
            JumpList.SetJumpList(application, jumpList);
            jumpList.Apply();
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            // Die Sprungliste ist eine Zugabe; ohne sie bleibt der Verlauf in der Anwendung.
            LogApplyFailed(_logger, exception);
        }
    }

    [LoggerMessage(EventId = 1740, Level = LogLevel.Warning, Message = "Sprungliste ließ sich nicht aktualisieren.")]
    private static partial void LogApplyFailed(ILogger logger, Exception exception);
}
