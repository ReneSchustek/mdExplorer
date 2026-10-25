namespace MdExplorer.App.ViewModels;

/// <summary>
/// Entscheidet, ob die Tag-Cloud zu sehen ist: aus der gespeicherten Wahl des Nutzers und der
/// Breite des Fensters.
/// </summary>
/// <remarks>
/// <para>
/// In einem schmalen Fenster nähme die Tag-Cloud der Vorschau den Platz, den sie zum Lesen
/// braucht. Sie blendet sich dort aus, ohne die gespeicherte Wahl zu ändern, und kommt beim
/// Verbreitern zurück.
/// </para>
/// <para>
/// Schaltet der Nutzer sie im schmalen Fenster ausdrücklich ein, bleibt sie, bis sich die Breite
/// das nächste Mal über die Grenze bewegt. Sonst nähme das Programm seine Entscheidung im selben
/// Augenblick wieder zurück.
/// </para>
/// </remarks>
internal sealed class TagCloudVisibility
{
    /// <summary>
    /// Unterhalb dieser Breite gilt das Fenster als schmal. 960 Einheiten entsprechen den 60 rem,
    /// ab denen die Web-Apps ihre Seitenleiste einklappen.
    /// </summary>
    public const double NarrowWidth = 960.0;

    private bool _shownWhileNarrow;

    /// <summary>Wahr, solange das Fenster schmaler als <see cref="NarrowWidth"/> ist.</summary>
    public bool IsNarrow { get; private set; }

    /// <summary>Ob die Tag-Cloud zu sehen ist.</summary>
    /// <param name="preferred">Die gespeicherte Wahl des Nutzers.</param>
    public bool IsShown(bool preferred) => preferred && (!IsNarrow || _shownWhileNarrow);

    /// <summary>Schaltet um, wie der Nutzer es sieht: Sichtbares verschwindet, Verborgenes erscheint.</summary>
    /// <param name="preferred">Die gespeicherte Wahl des Nutzers.</param>
    /// <returns>Die neue gespeicherte Wahl.</returns>
    public bool Toggle(bool preferred)
    {
        if (IsShown(preferred))
        {
            _shownWhileNarrow = false;
            return false;
        }
        _shownWhileNarrow = IsNarrow;
        return true;
    }

    /// <summary>Nimmt die neue Breite des Fensters auf.</summary>
    /// <param name="width">Die Breite in geräteunabhängigen Einheiten.</param>
    /// <returns><see langword="true"/>, wenn das Fenster dabei die Grenze überschritten hat.</returns>
    public bool UpdateWidth(double width)
    {
        bool narrow = width < NarrowWidth;
        if (narrow == IsNarrow)
        {
            return false;
        }
        IsNarrow = narrow;
        _shownWhileNarrow = false;
        return true;
    }
}
