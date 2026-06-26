using MdExplorer.App.Services;

namespace MdExplorer.App.Services.Help;

/// <summary>
/// Lese-/Schreib-Fassade über <see cref="UiSettingsStore"/> für die
/// persistierte Hilfe-Fenster-Geometrie. Kapselt das Lesen/Schreiben des
/// <see cref="UiLayout.HelpWindow"/>-Teilfelds, ohne dass der Aufrufer
/// die anderen Layout-Werte kennen muss.
/// </summary>
internal sealed class HelpWindowGeometryStore
{
    private readonly UiSettingsStore _backingStore;

    /// <summary>Erzeugt den Store mit dem zentralen <see cref="UiSettingsStore"/>.</summary>
    public HelpWindowGeometryStore(UiSettingsStore backingStore)
    {
        ArgumentNullException.ThrowIfNull(backingStore);
        _backingStore = backingStore;
    }

    /// <summary>Liefert die gespeicherte Geometrie oder <see langword="null"/>, wenn keine vorhanden.</summary>
    public WindowGeometry? Load()
    {
        return _backingStore.Load().HelpWindow;
    }

    /// <summary>Speichert die Geometrie additiv — andere Layout-Felder bleiben erhalten.</summary>
    public void Save(WindowGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        UiLayout current = _backingStore.Load();
        _backingStore.Save(current with { HelpWindow = geometry });
    }
}
