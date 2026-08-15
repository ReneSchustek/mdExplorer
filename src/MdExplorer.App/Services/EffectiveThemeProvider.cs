using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.App.Services;

/// <summary>
/// Leitet das geltende Erscheinungsbild aus Einstellung und System ab.
/// </summary>
/// <remarks>
/// Die einzige Stelle, an der diese Frage beantwortet wird. Zwei Stellen, die dasselbe
/// entscheiden, laufen irgendwann auseinander — und man sieht es erst an der einen Fläche,
/// die dann hell stehen bleibt.
/// </remarks>
internal sealed class EffectiveThemeProvider : IEffectiveThemeProvider
{
    private readonly ISettingsService _settings;
    private readonly ISystemThemeProvider _systemTheme;

    /// <summary>Erzeugt den Anbieter.</summary>
    public EffectiveThemeProvider(ISettingsService settings, ISystemThemeProvider systemTheme)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(systemTheme);

        _settings = settings;
        _systemTheme = systemTheme;
    }

    /// <inheritdoc />
    public bool IsDarkMode => _settings.Current.Appearance.Theme switch
    {
        AppTheme.Dark => true,
        AppTheme.Light => false,
        // Bei „System" entscheidet Windows. Fällt die Abfrage aus, ist Hell die
        // verträglichere Annahme: Dunkle Schrift auf hellem Grund bleibt lesbar,
        // umgekehrt verschwindet sie.
        _ => _systemTheme.IsDarkMode,
    };
}
