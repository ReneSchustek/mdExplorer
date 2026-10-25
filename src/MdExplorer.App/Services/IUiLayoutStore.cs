namespace MdExplorer.App.Services;

/// <summary>Liest und schreibt das gespeicherte <see cref="UiLayout"/>.</summary>
/// <remarks>
/// Fenster, die nur ihr eigenes Feld fortschreiben, brauchen weder Dateipfad noch Anzeigeort;
/// über diese Schnittstelle lassen sie sich ohne Datei auf der Platte prüfen.
/// </remarks>
internal interface IUiLayoutStore
{
    /// <summary>Lädt das Layout; ohne gespeicherten Stand <see cref="UiLayout.Default"/>.</summary>
    /// <returns>Das gespeicherte Layout.</returns>
    UiLayout Load();

    /// <summary>Schreibt das Layout.</summary>
    /// <param name="layout">Der vollständige neue Stand.</param>
    void Save(UiLayout layout);
}
