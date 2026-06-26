namespace MdExplorer.App.Services.Help;

/// <summary>
/// Hält den aktuell aktiven Hilfe-Kontext-Slug. Views aktualisieren ihn beim
/// Fokuswechsel, das Hilfefenster liest ihn beim <c>F1</c>-Aufruf und springt
/// zum passenden Kapitel.
/// </summary>
internal interface IHelpContextProvider
{
    /// <summary>
    /// Aktueller Anker-Slug. Standardwert ist <see cref="HelpContext.TableOfContents"/>,
    /// solange keine View einen spezifischeren Kontext gesetzt hat.
    /// </summary>
    string CurrentSlug { get; }

    /// <summary>
    /// Setzt den aktuellen Anker-Slug. <see langword="null"/> oder leer setzt
    /// auf den Default zurück.
    /// </summary>
    void SetSlug(string? slug);
}
