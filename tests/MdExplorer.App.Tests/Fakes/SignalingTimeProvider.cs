using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>Testuhr, die meldet, sobald jemand auf ihr zu warten beginnt.</summary>
/// <remarks>
/// Ein Hintergrunddienst beginnt seit .NET 10 ganz im Hintergrund. Wann er sein Warten anmeldet,
/// steht deshalb nach <c>StartAsync</c> nicht fest; ein zu frühes Vorstellen der Uhr ginge ins
/// Leere. Mit dem Signal stellt der Test die Uhr erst vor, wenn der Dienst tatsächlich wartet.
/// </remarks>
internal sealed class SignalingTimeProvider : FakeTimeProvider
{
    private readonly TaskCompletionSource _timerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Wird erfüllt, sobald der erste Zeitgeber angelegt ist.</summary>
    public Task TimerCreated => _timerCreated.Task;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ITimer timer = base.CreateTimer(callback, state, dueTime, period);
        _ = _timerCreated.TrySetResult();
        return timer;
    }
}
