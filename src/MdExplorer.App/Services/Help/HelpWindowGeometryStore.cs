namespace MdExplorer.App.Services.Help;

/// <summary>
/// Lese-/Schreib-Fassade über den <see cref="IUiLayoutStore"/> für die
/// persistierte Hilfe-Fenster-Geometrie. Kapselt das Lesen/Schreiben des
/// <see cref="UiLayout.HelpWindow"/>-Teilfelds, ohne dass der Aufrufer
/// die anderen Layout-Werte kennen muss.
/// </summary>
internal sealed class HelpWindowGeometryStore
{
    private readonly IUiLayoutStore _backingStore;

    /// <summary>Erzeugt den Store über dem zentralen Layout-Speicher.</summary>
    public HelpWindowGeometryStore(IUiLayoutStore backingStore)
    {
        ArgumentNullException.ThrowIfNull(backingStore);
        _backingStore = backingStore;
    }

    /// <summary>Liefert die gespeicherte Geometrie oder <see langword="null"/>, wenn keine vorhanden.</summary>
    public WindowGeometry? Load()
    {
        return _backingStore.Load().HelpWindow;
    }

    /// <summary>Speichert die Geometrie additiv; andere Layout-Felder bleiben erhalten.</summary>
    public void Save(WindowGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        UiLayout current = _backingStore.Load();
        _backingStore.Save(current with { HelpWindow = geometry });
    }
}
