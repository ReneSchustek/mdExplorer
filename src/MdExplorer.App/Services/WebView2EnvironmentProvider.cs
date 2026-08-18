using MdExplorer.Core;
using Microsoft.Web.WebView2.Core;

namespace MdExplorer.App.Services;

/// <summary>
/// Liefert die eine Browser-Umgebung der Anwendung — mit dem Benutzerdatenverzeichnis unter
/// <c>%LOCALAPPDATA%</c>.
/// </summary>
/// <remarks>
/// Ohne ausdrückliches Verzeichnis schreibt die Browser-Komponente neben die Programmdatei —
/// nach einer Installation also dorthin, wo der Benutzer nicht schreiben darf. Die Umgebung
/// wird einmal erzeugt und geteilt; zwei Umgebungen auf demselben Verzeichnis sind nicht
/// zulässig.
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
