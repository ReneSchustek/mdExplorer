using System.Diagnostics.CodeAnalysis;
using MdExplorer.Core.Hosting;

namespace MdExplorer.Core.Tests.Hosting;

/// <summary>
/// Die Hintergrunddienste fangen nur ab, was <see cref="BackgroundServiceWatchdog.IsRecoverable"/>
/// durchlässt. Hielte die Einteilung einen Abbruch für einen Fehler, protokollierte jedes Beenden
/// der App einen Absturz; schluckte sie Speichermangel, liefe der Prozess beschädigt weiter.
/// </summary>
[SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
    Justification = "Die Runtime-reservierten Exceptions entstehen hier nur als Prüfdaten für den Watchdog-Klassifizierer; geworfen werden sie nie.")]
public sealed class BackgroundServiceWatchdogTests
{
    private static readonly CancellationToken Running = CancellationToken.None;
    private static readonly CancellationToken Stopping = new(canceled: true);

    [Theory]
    [MemberData(nameof(RegularExceptions))]
    public void IsRecoverable_OnRegularException_ReturnsTrue(Exception exception)
    {
        Assert.True(BackgroundServiceWatchdog.IsRecoverable(exception, Running));
    }

    [Fact]
    public void IsRecoverable_OnOutOfMemoryException_ReturnsFalse()
    {
        Assert.False(BackgroundServiceWatchdog.IsRecoverable(new OutOfMemoryException(), Running));
    }

    [Fact]
    public void IsRecoverable_OnAccessViolationException_ReturnsFalse()
    {
        Assert.False(BackgroundServiceWatchdog.IsRecoverable(new AccessViolationException(), Running));
    }

    [Fact]
    public void IsRecoverable_OnCancellationWhileShuttingDown_ReturnsFalse()
    {
        Assert.False(BackgroundServiceWatchdog.IsRecoverable(new OperationCanceledException(), Stopping));
        Assert.False(BackgroundServiceWatchdog.IsRecoverable(new TaskCanceledException(), Stopping));
    }

    [Fact]
    public void IsRecoverable_OnForeignCancellation_ReturnsTrue()
    {
        // Ein Zeitlimit oder ein verknüpftes Token einer Bibliothek ist kein Herunterfahren; ließe
        // der Dienst es durch, führe mit StopHost die ganze Anwendung herunter.
        Assert.True(BackgroundServiceWatchdog.IsRecoverable(new TaskCanceledException(), Running));
    }

    [Fact]
    public void IsRecoverable_OnNull_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => BackgroundServiceWatchdog.IsRecoverable(null!, Running));
    }

    public static TheoryData<Exception> RegularExceptions =>
    [
        new ArgumentException("x"),
        new InvalidOperationException("x"),
        new IOException("x"),
        new UnauthorizedAccessException("x"),
        new NullReferenceException("x"),
    ];
}
