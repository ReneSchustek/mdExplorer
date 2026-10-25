using System.Data.Common;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Die Fehler, mit denen ein Lesezugriff auf den Index rechnen muss.
/// </summary>
/// <remarks>
/// <para>
/// SQLite meldet eine belegte oder beschädigte Datenbank als <see cref="DbException"/>, EF Core
/// einen schon entsorgten oder falsch benutzten Kontext als <see cref="InvalidOperationException"/>.
/// Ein Bereich, der nur eine der beiden fängt, bleibt bei der anderen in „lädt“ stehen, und die
/// Ausnahme verschwindet im verworfenen Task.
/// </para>
/// <para>
/// Abbruch gehört nicht dazu: Er ist kein Fehler, sondern ein neuerer Klick oder das Beenden der
/// Anwendung, und jeder Bereich behandelt ihn eigens.
/// </para>
/// </remarks>
internal static class DataLoadFailure
{
    /// <summary>Ob <paramref name="exception"/> ein erwartbarer Lesefehler ist, den der Bereich anzeigt.</summary>
    /// <param name="exception">Die gefangene Ausnahme.</param>
    /// <returns><see langword="true"/> für Datenbank- und Zustandsfehler des Index.</returns>
    public static bool IsExpected(Exception exception) =>
        exception is DbException or InvalidOperationException;
}
