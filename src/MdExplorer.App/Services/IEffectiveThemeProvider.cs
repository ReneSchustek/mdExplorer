namespace MdExplorer.App.Services;

/// <summary>
/// Sagt, welches Erscheinungsbild tatsächlich gilt.
/// </summary>
/// <remarks>
/// Die Wahl des Nutzers schlägt das System — und genau daran fehlte es: Die Vorschau
/// fragte Windows, während die übrige Oberfläche der Einstellung folgte. Wer „Dunkel"
/// wählte, während Windows hell steht, bekam eine weiße Fläche mitten in einer dunklen
/// Anwendung. Beide Seiten fragen jetzt dieselbe Stelle.
/// </remarks>
internal interface IEffectiveThemeProvider
{
    /// <summary><see langword="true"/>, wenn die dunkle Belegung gilt.</summary>
    bool IsDarkMode { get; }
}
