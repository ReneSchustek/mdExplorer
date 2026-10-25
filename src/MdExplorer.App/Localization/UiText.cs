using System.Resources;
using MdExplorer.Core.Text;

namespace MdExplorer.App.Localization;

/// <summary>
/// Anzeigetexte der Anwendung aus <c>Strings/Resources.resx</c>, für ViewModels und Code-Behind.
/// Im XAML steht stattdessen <see cref="LocExtension"/>.
/// </summary>
internal static class UiText
{
    private static readonly TextCatalog Catalog =
        new(new ResourceManager("MdExplorer.App.Strings.Resources", typeof(UiText).Assembly));

    /// <summary>Liefert den Text zu <paramref name="key"/>.</summary>
    /// <param name="key">Schlüssel aus der Ressourcendatei.</param>
    /// <returns>Der Text, oder <c>[?key?]</c>, wenn der Schlüssel fehlt.</returns>
    public static string Get(string key) => Catalog.Get(key);

    /// <summary>Liefert den Text zu <paramref name="key"/> mit eingesetzten Werten.</summary>
    /// <param name="key">Schlüssel aus der Ressourcendatei.</param>
    /// <param name="args">Werte für die Platzhalter.</param>
    /// <returns>Der Text mit den Werten.</returns>
    public static string Format(string key, params object?[] args) => Catalog.Format(key, args);
}
