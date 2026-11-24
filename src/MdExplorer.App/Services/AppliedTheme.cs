using Microsoft.Win32;

namespace MdExplorer.App.Services;

/// <summary>
/// Das Erscheinungsbild, mit dem die Oberfläche gerade aufgebaut ist.
/// </summary>
/// <remarks>
/// Windows meldet viele Änderungen als „allgemein“, nicht nur den Wechsel zwischen Hell und Dunkel.
/// Neu angewendet wird nur, wenn sich das geltende Erscheinungsbild wirklich von diesem unterscheidet;
/// sonst baute jede Kleinigkeit die Vorschau neu.
/// </remarks>
/// <param name="IsDarkMode">Ob die dunkle Belegung gilt.</param>
/// <param name="IsHighContrast">Ob der Kontrastmodus gilt.</param>
internal sealed record AppliedTheme(bool IsDarkMode, bool IsHighContrast)
{
    /// <summary>Hält fest, was der Anbieter jetzt als geltend meldet.</summary>
    /// <param name="effectiveTheme">Der Anbieter des geltenden Erscheinungsbilds.</param>
    public static AppliedTheme From(IEffectiveThemeProvider effectiveTheme)
    {
        ArgumentNullException.ThrowIfNull(effectiveTheme);
        return new AppliedTheme(effectiveTheme.IsDarkMode, effectiveTheme.IsHighContrast);
    }

    /// <summary>Ob eine Änderung in Windows das Erscheinungsbild betreffen kann.</summary>
    /// <remarks>Den Wechsel zwischen Hell und Dunkel meldet Windows als allgemeine Änderung.</remarks>
    /// <param name="category">Die Art der gemeldeten Änderung.</param>
    public static bool MayChangeTheTheme(UserPreferenceCategory category) => category == UserPreferenceCategory.General;

    /// <summary>Ob das geltende Erscheinungsbild ein anderes ist als dieses.</summary>
    /// <param name="effectiveTheme">Der Anbieter des geltenden Erscheinungsbilds.</param>
    public bool DiffersFrom(IEffectiveThemeProvider effectiveTheme) => this != From(effectiveTheme);
}
