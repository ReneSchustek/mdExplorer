namespace MdExplorer.App.Hosting;

/// <summary>Was mit einer Ausnahme geschieht, die bis zum Dispatcher durchgefallen ist.</summary>
internal enum UnhandledExceptionAction
{
    /// <summary>Der Start ist gescheitert: melden und die Anwendung beenden.</summary>
    AbortStartup,

    /// <summary>Melden und weiterlaufen.</summary>
    ShowDialog,

    /// <summary>Nur protokollieren.</summary>
    LogOnly,
}

/// <summary>
/// Entscheidet, wie die Anwendung auf eine Ausnahme reagiert, die niemand gefangen hat.
/// </summary>
/// <remarks>
/// <para>
/// Vor dem Ende des Starts gibt es kein Hauptfenster, an dem ein Weiterlaufen hinge: Das
/// Startbild bliebe stehen, und der Prozess liefe unsichtbar weiter, weil die Anwendung erst mit
/// dem Hauptfenster endet. Eine solche Ausnahme beendet deshalb den Start, gleich welcher Art sie
/// ist.
/// </para>
/// <para>
/// Danach bekommt ein Fehler, der sich wiederholt (Layout, Zeitgeber), nicht für jedes Auftreten
/// einen eigenen Dialog: Es steht höchstens einer zur Zeit, und derselbe Fehler erscheint nur
/// einmal je Lauf. Protokolliert wird jedes Auftreten.
/// </para>
/// <para>Nur vom UI-Thread aufzurufen; dort laufen alle Dispatcher-Ausnahmen auf.</para>
/// </remarks>
internal sealed class UnhandledExceptionPolicy
{
    private readonly HashSet<string> _shownFailures = new(StringComparer.Ordinal);
    private bool _startupCompleted;
    private bool _startupAborted;
    private bool _dialogOpen;

    /// <summary>Meldet, dass das Hauptfenster steht.</summary>
    public void MarkStartupCompleted() => _startupCompleted = true;

    /// <summary>Entscheidet über <paramref name="exception"/>.</summary>
    /// <param name="exception">Die unbehandelte Ausnahme.</param>
    public UnhandledExceptionAction Decide(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (!_startupCompleted)
        {
            // Nur der erste Fehler meldet sich: Was beim Beenden noch nachfällt, ist Folge.
            if (_startupAborted)
            {
                return UnhandledExceptionAction.LogOnly;
            }
            _startupAborted = true;
            return UnhandledExceptionAction.AbortStartup;
        }

        if (_dialogOpen || !_shownFailures.Add(Signature(exception)))
        {
            return UnhandledExceptionAction.LogOnly;
        }
        _dialogOpen = true;
        return UnhandledExceptionAction.ShowDialog;
    }

    /// <summary>Meldet, dass der Fehlerdialog geschlossen wurde.</summary>
    public void DialogClosed() => _dialogOpen = false;

    // Art und Meldung genügen: Derselbe Fehler aus demselben Anlass trägt beide unverändert.
    private static string Signature(Exception exception) => exception.GetType().FullName + "\n" + exception.Message;
}
