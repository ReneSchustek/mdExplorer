namespace MdExplorer.App.Services;

/// <summary>
/// Sagt, welches Erscheinungsbild tatsächlich gilt.
/// </summary>
/// <remarks>
/// Die Wahl des Nutzers schlägt das System. Fragte die Vorschau Windows, während die übrige
/// Oberfläche der Einstellung folgt, bekäme, wer „Dunkel" wählt, während Windows hell steht,
/// eine weiße Fläche mitten in einer dunklen Anwendung. Deshalb fragen beide Seiten dieselbe
/// Stelle.
/// </remarks>
internal interface IEffectiveThemeProvider
{
    /// <summary><see langword="true"/>, wenn die dunkle Belegung gilt.</summary>
    bool IsDarkMode { get; }
}
