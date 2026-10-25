using MdExplorer.Core.Abstractions;

namespace MdExplorer.Core.Diagnostics;

/// <summary>
/// Prozessweiter Zustandshalter für <see cref="IBackgroundServiceStatus"/>. Als Singleton
/// registriert: Die Dienste melden aus dem Hintergrund, die Oberfläche liest. Deshalb ist der
/// Zugriff gesperrt und <see cref="Changed"/> feuert außerhalb der Sperre.
/// </summary>
public sealed class BackgroundServiceStatus : IBackgroundServiceStatus
{
    private readonly object _gate = new();
    private readonly HashSet<BackgroundServiceKind> _stopped = [];

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyCollection<BackgroundServiceKind> Stopped
    {
        get
        {
            lock (_gate)
            {
                return [.. _stopped.Order()];
            }
        }
    }

    /// <inheritdoc />
    public void ReportStopped(BackgroundServiceKind service)
    {
        bool added;
        lock (_gate)
        {
            added = _stopped.Add(service);
        }
        if (added)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
