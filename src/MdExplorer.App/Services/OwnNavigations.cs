namespace MdExplorer.App.Services;

/// <summary>
/// Erkennt die Navigationen, die die Vorschau selbst per <c>NavigateToString</c> anstößt.
/// </summary>
/// <remarks>
/// <para>
/// Laut Dokumentation von WebView2 gelten auch Navigationen aus der Anwendung als vom Nutzer
/// ausgelöst. Am Kennzeichen „Klick“ allein ließe sich das eigene Dokument deshalb nicht sicher
/// erkennen; die Vorschau bliebe leer, sobald eine Laufzeitfassung das Kennzeichen anders setzt.
/// </para>
/// <para>
/// Vor jedem Laden wird eine Navigation vorgemerkt, die nächste passende verbraucht sie. Gezählt
/// wird, weil zwei rasch aufeinander folgende Ladevorgänge zwei Navigationen auslösen. Eine
/// Navigation auf ein Textdokument ohne Vormerkung, etwa aus einer Weiterleitung im Dokument,
/// ist nicht das eigene. Aufgerufen wird nur auf dem UI-Thread.
/// </para>
/// </remarks>
internal sealed class OwnNavigations
{
    private int _expected;

    /// <summary>Merkt vor, dass gleich das eigene Dokument geladen wird.</summary>
    public void Expect() => _expected++;

    /// <summary>Ob die Navigation das vorgemerkte eigene Dokument ist; wenn ja, ist die Vormerkung verbraucht.</summary>
    /// <param name="uri">Ziel der Navigation, wie WebView2 es meldet.</param>
    /// <returns><see langword="true"/> für das eigene Dokument.</returns>
    public bool TryConsume(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (_expected == 0 || !PreviewNavigationPolicy.IsStringDocument(uri))
        {
            return false;
        }
        _expected--;
        return true;
    }
}
