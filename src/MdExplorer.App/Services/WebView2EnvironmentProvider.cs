using MdExplorer.Core;
using Microsoft.Web.WebView2.Core;

namespace MdExplorer.App.Services;

/// <summary>
/// Liefert die eine Browser-Umgebung der Anwendung, mit dem Benutzerdatenverzeichnis unter
/// <c>%LOCALAPPDATA%</c>.
/// </summary>
/// <remarks>
/// <para>
/// Ohne ausdrückliches Verzeichnis schreibt die Browser-Komponente neben die Programmdatei,
/// nach einer Installation also dorthin, wo der Benutzer nicht schreiben darf. Die Umgebung
/// wird einmal erzeugt und geteilt; zwei Umgebungen auf demselben Verzeichnis sind nicht
/// zulässig. Scheitert die Erzeugung, etwa weil die Laufzeit fehlt, versucht das nächste Öffnen
/// einer Fläche es erneut.
/// </para>
/// <para>
/// Statisch und nicht über die Abhängigkeitsverwaltung: Die Umgebung gilt für den ganzen
/// Prozess, und ihre Nutzer sind Ansichten, die ihre Browser-Fläche im Code-Behind starten.
/// Die Logik dahinter steckt in <see cref="RetryingTaskCache{T}"/> und ist dort geprüft.
/// </para>
/// </remarks>
internal static class WebView2EnvironmentProvider
{
    private static readonly RetryingTaskCache<CoreWebView2Environment> Shared = new(
        () => CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: AppPaths.Default.WebView2DataDirectory));

    /// <summary>
    /// Liefert die geteilte Umgebung und erzeugt sie beim ersten Aufruf oder nach einem gescheiterten.
    /// </summary>
    /// <returns>Die Umgebung mit dem beschreibbaren Benutzerdatenverzeichnis.</returns>
    public static Task<CoreWebView2Environment> GetAsync() => Shared.GetAsync();
}
