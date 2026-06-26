namespace MdExplorer.App.Services.Help;

/// <summary>
/// Thread-safer Default-Speicher für den aktuellen Hilfe-Kontext. Lese-/
/// Schreibzugriff erfolgt über <see cref="Interlocked.Exchange{T}(ref T, T)"/>
/// und <see cref="Volatile.Read{T}(ref T)"/>, sodass das Lesen aus dem
/// Hilfe-Fenster (UI-Thread) und das Schreiben aus dem fokussierenden View
/// (ebenfalls UI-Thread) konsistent bleibt — und auch dann sicher, wenn ein
/// Hintergrund-Worker den Kontext setzt.
/// </summary>
internal sealed class HelpContextProvider : IHelpContextProvider
{
    private string _currentSlug = HelpContext.TableOfContents;

    /// <inheritdoc />
    public string CurrentSlug => Volatile.Read(ref _currentSlug);

    /// <inheritdoc />
    public void SetSlug(string? slug)
    {
        string normalized = string.IsNullOrWhiteSpace(slug)
            ? HelpContext.TableOfContents
            : slug.Trim();
        _ = Interlocked.Exchange(ref _currentSlug, normalized);
    }
}
