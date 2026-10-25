namespace MdExplorer.App.Services;

/// <summary>
/// Hält das Ergebnis einer einmaligen, asynchronen Erzeugung. Scheitert sie, versucht der nächste
/// Aufruf es erneut, statt den Fehler bis zum Neustart zu wiederholen.
/// </summary>
/// <remarks>
/// <see cref="Lazy{T}"/> mit einem Task merkt sich auch einen gescheiterten Task. Für die
/// Browser-Umgebung hieße das: Fehlt die WebView2-Laufzeit beim ersten Öffnen und wird danach
/// installiert, bliebe jede Fläche leer, bis die Anwendung neu startet. Gleichzeitige Aufrufer
/// teilen sich eine laufende Erzeugung; zwei Umgebungen auf demselben Verzeichnis sind nicht
/// zulässig.
/// </remarks>
/// <typeparam name="T">Art des erzeugten Werts.</typeparam>
internal sealed class RetryingTaskCache<T>(Func<Task<T>> create)
{
    private readonly Lock _lock = new();
    private Task<T>? _current;

    /// <summary>Liefert den Wert und erzeugt ihn, wenn es noch keinen gibt oder der letzte Versuch scheiterte.</summary>
    /// <returns>Die laufende oder abgeschlossene Erzeugung.</returns>
    public Task<T> GetAsync()
    {
        lock (_lock)
        {
            if (_current is null || _current.IsFaulted || _current.IsCanceled)
            {
                _current = create();
            }
            return _current;
        }
    }
}
