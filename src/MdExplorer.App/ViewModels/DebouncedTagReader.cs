using System.Collections.ObjectModel;
using System.Windows.Data;
using MdExplorer.Parser.Abstractions;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Liest die Kennzeichnungen aus dem Editortext, beim Tippen erst nach einer kurzen Pause.
/// </summary>
/// <remarks>
/// Ohne die Pause liefe die Erkennung bei jedem Zeichen über den ganzen Text. Jeder Dokumentwechsel
/// beginnt eine neue Fassung (<see cref="Reset"/>); ein Zeitgeber, der noch zur vorigen gehört,
/// trägt nichts mehr ein.
/// </remarks>
internal sealed class DebouncedTagReader : IDisposable
{
    /// <summary>
    /// Wartezeit nach dem letzten Tastenanschlag, bevor die Tags neu gelesen werden.
    /// </summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private readonly ITagExtractor _tagExtractor;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();

    // Ein object und kein Lock: EnableCollectionSynchronization sperrt über Monitor und nimmt
    // keinen System.Threading.Lock an.
    private readonly object _tagsGate = new();

    private ITimer? _timer;

    // Zählt jeden Neubeginn. Ein Zeitgeber trägt die Zahl, unter der er angelegt wurde; feuert
    // er danach, gehört sein Text zu einem Stand, der nicht mehr gilt.
    private long _generation;
    private bool _disposed;

    /// <summary>Erzeugt die Erkennung.</summary>
    public DebouncedTagReader(ITagExtractor tagExtractor, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(tagExtractor);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _tagExtractor = tagExtractor;
        _timeProvider = timeProvider;
        Tags = [];
        BindingOperations.EnableCollectionSynchronization(Tags, _tagsGate);
    }

    /// <summary>Die erkannten Tag-Namen in Original-Schreibweise.</summary>
    public ObservableCollection<string> Tags { get; }

    /// <summary>Liest die Tags nach der Wartezeit; ein noch laufender Zeitgeber wird ersetzt.</summary>
    public void Schedule(string text)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            DisposeTimer();
            DebouncedText pending = new(_generation, text);
            _timer = _timeProvider.CreateTimer(OnElapsed, pending, Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Beginnt eine neue Fassung und liest die Tags von <paramref name="text"/> sofort.</summary>
    /// <remarks>Für Laden, Verwerfen und Leeren: Ein wartender Zeitgeber trüge sonst den alten Text nach.</remarks>
    public void Reset(string text)
    {
        lock (_gate)
        {
            _generation++;
            DisposeTimer();
        }
        Replace(_tagExtractor.ExtractFromText(text));
    }

    /// <summary>Hält den Zeitgeber an.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            DisposeTimer();
        }
    }

    // Läuft auf einem Thread des Zeitgebers. Unter der Sperre geprüft und übernommen, damit ein
    // Neubeginn auf dem UI-Thread nicht zwischen Prüfung und Übernahme fällt.
    private void OnElapsed(object? state)
    {
        if (state is not DebouncedText pending)
        {
            return;
        }

        IReadOnlyList<string> extracted = _tagExtractor.ExtractFromText(pending.Text);
        lock (_gate)
        {
            if (_disposed || pending.Generation != _generation)
            {
                return;
            }
            Replace(extracted);
        }
    }

    // Die Sperre gehört zur Synchronisation, die der Konstruktor für Tags einrichtet; der Zeitgeber
    // schreibt von einem anderen Thread als die Oberfläche.
    private void Replace(IReadOnlyList<string> tags)
    {
        lock (_tagsGate)
        {
            Tags.Clear();
            foreach (string tag in tags)
            {
                Tags.Add(tag);
            }
        }
    }

    private void DisposeTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Text für die Tag-Erkennung samt der Fassung, zu der er gehört.</summary>
    /// <param name="Generation">Zählerstand beim Anlegen des Zeitgebers.</param>
    /// <param name="Text">Der Text zu diesem Zeitpunkt.</param>
    private sealed record DebouncedText(long Generation, string Text);
}
