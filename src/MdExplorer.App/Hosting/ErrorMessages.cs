using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.IO;
using MdExplorer.App.Localization;
using Microsoft.Extensions.Options;

namespace MdExplorer.App.Hosting;

/// <summary>
/// Texte der Fehlerdialoge für einen gescheiterten Start und für Ausnahmen, die niemand gefangen
/// hat.
/// </summary>
internal static class ErrorMessages
{
    /// <summary>Meldung für einen gescheiterten Start.</summary>
    /// <param name="exception">Die Ausnahme, an der der Start scheiterte.</param>
    public static string StartupFailure(Exception exception)
        => UiText.Format("Startup_Failure_Message", StartupHint(exception));

    /// <summary>Meldung für eine unbehandelte Ausnahme.</summary>
    /// <param name="exception">Die Ausnahme.</param>
    /// <param name="isTerminating">Ob der Prozess danach endet.</param>
    public static string Unhandled(Exception exception, bool isTerminating)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string hint = isTerminating ? UiText.Get("Error_Fatal_Hint") : UiText.Get("Error_Unhandled_Hint");
        return UiText.Format("Error_Details", hint, exception.GetType().Name, exception.Message);
    }

    /// <summary>Titel zur Meldung aus <see cref="Unhandled"/>.</summary>
    /// <param name="isTerminating">Ob der Prozess danach endet.</param>
    public static string UnhandledTitle(bool isTerminating)
        => isTerminating ? UiText.Get("Error_Fatal_Title") : UiText.Get("Error_Unhandled_Title");

    /// <summary>Ein Satz dazu, woran der Start gescheitert ist.</summary>
    /// <param name="exception">Die Ausnahme, an der der Start scheiterte.</param>
    public static string StartupHint(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Die Reihenfolge trägt: Die Prüfung der Optionen meldet sich mit einer eigenen Art und
        // muss vor der allgemeinen Konfigurationsart stehen.
        return exception switch
        {
            OptionsValidationException => UiText.Get("Startup_Hint_InvalidOptions"),
            ValidationException => UiText.Get("Startup_Hint_InvalidSettings"),
            DbException => UiText.Get("Startup_Hint_Database"),
            IOException => UiText.Get("Startup_Hint_FileAccess"),
            UnauthorizedAccessException => UiText.Get("Startup_Hint_AccessDenied"),
            InvalidOperationException => UiText.Get("Startup_Hint_Configuration"),
            _ => UiText.Get("Startup_Hint_Unexpected"),
        };
    }
}
