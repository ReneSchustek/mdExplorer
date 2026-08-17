using MdExplorer.Core;
using Microsoft.Web.WebView2.Core;

namespace MdExplorer.App.Services;

/// <summary>
/// Liefert die eine Browser-Umgebung der Anwendung — mit dem Benutzerdatenverzeichnis unter
/// <c>%LOCALAPPDATA%</c>.
/// </summary>
/// <remarks>
/// <para>
/// Es gibt sie, weil drei Ansichten dieselbe Umgebung brauchen und zwei davon sie bis zum
/// 17.08.2026 gar nicht angegeben haben. Ohne Angabe schreibt die Browser-Komponente neben
/// die Programmdatei; nach einer Installation liegt die in <c>C:\Program Files</c>, und der
/// Aufruf scheitert mit „Zugriff verweigert". Verweisgraph und Hilfe blieben dadurch in
/// jeder installierten Fassung leer, während beide im Entwicklungslauf einwandfrei liefen.
/// </para>
/// <para>
/// Die Umgebung wird genau einmal erzeugt und danach geteilt: Zwei Umgebungen auf demselben
/// Verzeichnis sind ohnehin nicht zulässig.
/// </para>
/// </remarks>
internal static class WebView2EnvironmentProvider
{
    private static readonly Lazy<Task<CoreWebView2Environment>> Shared = new(
        () => CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: AppPaths.GetWebView2DataDirectory()),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Liefert die geteilte Umgebung und erzeugt sie beim ersten Aufruf.
    /// </summary>
    /// <returns>Die Umgebung mit dem beschreibbaren Benutzerdatenverzeichnis.</returns>
    public static Task<CoreWebView2Environment> GetAsync() => Shared.Value;
}
