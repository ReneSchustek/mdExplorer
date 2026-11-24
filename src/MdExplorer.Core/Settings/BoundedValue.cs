using MdExplorer.Core.Models;

namespace MdExplorer.Core.Settings;

/// <summary>Eine begrenzte Zahlen-Einstellung.</summary>
/// <param name="Field">Welches Feld.</param>
/// <param name="Value">Der aktuelle Wert.</param>
/// <param name="Minimum">Kleinster zulässiger Wert.</param>
/// <param name="Maximum">Größter zulässiger Wert.</param>
/// <param name="Fallback">Vorgabe, die beim Laden an die Stelle eines Werts außerhalb tritt.</param>
public sealed record BoundedValue(SettingsField Field, int Value, int Minimum, int Maximum, int Fallback)
{
    /// <summary><see langword="true"/>, wenn der Wert innerhalb seiner Grenzen liegt.</summary>
    public bool IsWithinBounds => Value >= Minimum && Value <= Maximum;
}
