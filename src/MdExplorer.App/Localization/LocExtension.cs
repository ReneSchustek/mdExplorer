using System.Windows.Markup;

namespace MdExplorer.App.Localization;

/// <summary>
/// Setzt im XAML einen Text aus <c>Strings/Resources.resx</c> ein:
/// <c>Title="{l:Loc Settings_Title}"</c>.
/// </summary>
/// <remarks>
/// Der Text wird einmal beim Laden des Elements gelesen. Ein Sprachwechsel zur Laufzeit ist
/// nicht vorgesehen; dafür bräuchte es eine Bindung statt eines festen Werts.
/// </remarks>
[MarkupExtensionReturnType(typeof(string))]
internal sealed class LocExtension : MarkupExtension
{
    /// <summary>Erzeugt die Erweiterung für einen Schlüssel.</summary>
    /// <param name="key">Schlüssel aus der Ressourcendatei.</param>
    public LocExtension(string key)
    {
        Key = key;
    }

    /// <summary>Schlüssel aus der Ressourcendatei.</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; }

    /// <inheritdoc/>
    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.Get(Key);
}
