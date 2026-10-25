using System.Globalization;
using System.Resources;

namespace MdExplorer.Core.Text;

/// <summary>
/// Liest Anzeigetexte aus einer Ressourcendatei. Jede Baugruppe mit Oberfläche hält ihre Texte
/// in <c>Strings/Resources.resx</c> und greift über einen eigenen Katalog darauf zu.
/// </summary>
/// <remarks>
/// Ein unbekannter Schlüssel liefert <c>[?Schlüssel?]</c> statt einer Ausnahme: Ein Tippfehler
/// soll in der Oberfläche auffallen, aber kein Fenster am Öffnen hindern. Dass jeder verwendete
/// Schlüssel existiert, prüft ein Wächter-Test.
/// </remarks>
public sealed class TextCatalog
{
    private readonly ResourceManager _resources;

    /// <summary>Erzeugt einen Katalog über die Ressourcen einer Baugruppe.</summary>
    /// <param name="resources">Die Ressourcen, aus denen gelesen wird.</param>
    public TextCatalog(ResourceManager resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        _resources = resources;
    }

    /// <summary>Liefert den Text zu <paramref name="key"/> in der aktuellen Oberflächensprache.</summary>
    /// <param name="key">Schlüssel aus der Ressourcendatei.</param>
    /// <returns>Der Text, oder <c>[?key?]</c>, wenn der Schlüssel fehlt.</returns>
    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        return _resources.GetString(key, CultureInfo.CurrentUICulture) ?? $"[?{key}?]";
    }

    /// <summary>Liefert den Text zu <paramref name="key"/> mit eingesetzten Werten.</summary>
    /// <param name="key">Schlüssel aus der Ressourcendatei.</param>
    /// <param name="args">Werte für die Platzhalter <c>{0}</c>, <c>{1}</c> und so fort.</param>
    /// <returns>Der Text mit den Werten, formatiert nach der aktuellen Kultur.</returns>
    public string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
