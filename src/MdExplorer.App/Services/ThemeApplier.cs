using System.Collections.ObjectModel;
using System.Windows;
using MdExplorer.Core.Models;

namespace MdExplorer.App.Services;

/// <summary>
/// Tauscht die Farbbelegung der Oberfläche zur Laufzeit.
/// </summary>
/// <remarks>
/// Getauscht wird genau ein Wörterbuch: die Belegung. Abstände, Radien und Schriftgrößen
/// bleiben stehen, weil sie mit dem Erscheinungsbild nichts zu tun haben. Der Austausch
/// erfolgt über die Position in der Liste, damit die Reihenfolge erhalten bleibt — ein
/// Anhängen ans Ende würde später geladene Wörterbücher überschreiben.
/// </remarks>
internal sealed class ThemeApplier
{
    private const string PaletteMarkerKey = "AppBackgroundBrush";

    private static readonly Uri LightPalette = new("/Themes/Light.xaml", UriKind.Relative);
    private static readonly Uri DarkPalette = new("/Themes/Dark.xaml", UriKind.Relative);

    private readonly IEffectiveThemeProvider _effectiveTheme;
    private readonly Func<Uri, ResourceDictionary> _paletteLoader;

    /// <summary>
    /// Erzeugt den Umschalter mit dem regulären Lader.
    /// </summary>
    public ThemeApplier(IEffectiveThemeProvider effectiveTheme)
        : this(effectiveTheme, uri => new ResourceDictionary { Source = uri })
    {
    }

    /// <summary>
    /// Erzeugt den Umschalter mit einem eigenen Lader.
    /// </summary>
    /// <remarks>
    /// Das Laden ist herausgezogen, weil eine Ressourcenadresse nur innerhalb einer
    /// laufenden Anwendung auflösbar ist. Auswahl und Austausch — das, woran ein Fehler
    /// die halbe Oberfläche unlesbar macht — bleiben so ohne Fenster prüfbar.
    /// </remarks>
    public ThemeApplier(IEffectiveThemeProvider effectiveTheme, Func<Uri, ResourceDictionary> paletteLoader)
    {
        _effectiveTheme = effectiveTheme ?? throw new ArgumentNullException(nameof(effectiveTheme));
        _paletteLoader = paletteLoader ?? throw new ArgumentNullException(nameof(paletteLoader));
    }

    /// <summary>
    /// Setzt die geltende Belegung in den angegebenen Wörterbüchern.
    /// </summary>
    /// <param name="dictionaries">Wörterbücher der Anwendung.</param>
    public void Apply(Collection<ResourceDictionary> dictionaries)
    {
        ArgumentNullException.ThrowIfNull(dictionaries);

        ResourceDictionary palette = _paletteLoader(_effectiveTheme.IsDarkMode ? DarkPalette : LightPalette);

        int existingIndex = IndexOfPalette(dictionaries);
        if (existingIndex >= 0)
        {
            dictionaries[existingIndex] = palette;
            return;
        }

        dictionaries.Add(palette);
    }

    /// <summary>
    /// Findet die bereits geladene Belegung an ihrem Merkmalsschlüssel.
    /// </summary>
    /// <remarks>
    /// Über einen Schlüssel statt über die Quelladresse: Die Adresse ist nach dem Laden
    /// nicht in jedem Fall vergleichbar, der Schlüsselsatz dagegen ist verbindlich und
    /// durch einen Test abgesichert.
    /// </remarks>
    private static int IndexOfPalette(Collection<ResourceDictionary> dictionaries)
    {
        for (int index = 0; index < dictionaries.Count; index++)
        {
            if (dictionaries[index].Contains(PaletteMarkerKey))
            {
                return index;
            }
        }

        return -1;
    }
}
