namespace MdExplorer.App.Services;

/// <summary>
/// Liefert den aktiven System-Theme-Status (Light/Dark). Eigene Abstraktion,
/// damit der <see cref="PreviewHtmlBuilder"/> in Tests deterministisch beide Themes prüfen kann.
/// </summary>
internal interface ISystemThemeProvider
{
    /// <summary><see langword="true"/>, wenn das Windows-Apps-Theme „Dark" aktiv ist.</summary>
    bool IsDarkMode { get; }
}
