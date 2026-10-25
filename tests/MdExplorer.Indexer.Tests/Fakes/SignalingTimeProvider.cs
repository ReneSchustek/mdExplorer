using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Indexer.Tests.Fakes;

/// <summary>
/// Testuhr, die jeden angelegten Zeitgeber zählt und festhält, ob er freigegeben wurde.
/// </summary>
/// <remarks>
/// <para>
/// Eine Testuhr löst einen Zeitgeber nur aus, wenn sie nach seinem Anlegen vorgestellt wird. Ein
/// Hintergrunddienst legt seinen Zeitgeber aber irgendwann an; wer vorher vorstellt, verfehlt
/// ihn. Statt mit kurzen Pausen zu raten, wartet ein Test auf das Anlegen und stellt dann vor.
/// </para>
/// <para>
/// Ein periodischer Zeitgeber (<c>PeriodicTimer</c> des Abgleichs) ist an der endlichen Periode
/// erkennbar; die Entprellung und die Wartezeit zwischen zwei Leseversuchen laufen einmalig.
/// </para>
/// </remarks>
internal sealed class SignalingTimeProvider() : FakeTimeProvider(new DateTimeOffset(2026, 6, 7, 12, 0, 0, TimeSpan.Zero))
{
    private readonly object _gate = new();
    private readonly List<RecordedTimer> _timers = [];

    /// <summary>Jeder angelegte Zeitgeber.</summary>
    public EventCounter TimersCreated { get; } = new();

    /// <summary>Jeder angelegte periodische Zeitgeber.</summary>
    public EventCounter PeriodicTimersCreated { get; } = new();

    /// <summary>Alle bisher angelegten Zeitgeber.</summary>
    public IReadOnlyList<RecordedTimer> Timers
    {
        get
        {
            lock (_gate)
            {
                return [.. _timers];
            }
        }
    }

    public int PeriodicTimerCount => (int)PeriodicTimersCreated.Mark;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        RecordedTimer timer = new(base.CreateTimer(callback, state, dueTime, period), period != Timeout.InfiniteTimeSpan);
        lock (_gate)
        {
            _timers.Add(timer);
        }
        TimersCreated.Signal();
        if (timer.IsPeriodic)
        {
            PeriodicTimersCreated.Signal();
        }
        return timer;
    }

    /// <summary>Ein angelegter Zeitgeber; merkt sich, ob er freigegeben wurde.</summary>
    internal sealed class RecordedTimer(ITimer inner, bool isPeriodic) : ITimer
    {
        public bool IsPeriodic { get; } = isPeriodic;

        public bool IsDisposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(dueTime, period);

        public void Dispose()
        {
            IsDisposed = true;
            inner.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return inner.DisposeAsync();
        }
    }
}
