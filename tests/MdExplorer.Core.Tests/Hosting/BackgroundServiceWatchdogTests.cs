using System.Diagnostics.CodeAnalysis;
using MdExplorer.Core.Hosting;

namespace MdExplorer.Core.Tests.Hosting;

/// <summary>Tests fuer den <see cref="BackgroundServiceWatchdog"/>.</summary>
[SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
    Justification = "Wir konstruieren die Runtime-reservierten Exceptions ausschliesslich, um den Watchdog-Klassifizierer zu testen; sie werden nie geworfen.")]
public sealed class BackgroundServiceWatchdogTests
{
    [Fact]
    public void IsRecoverable_OnRegularException_ReturnsTrue()
    {
        Assert.True(BackgroundServiceWatchdog.IsRecoverable(new ArgumentException("x")));
        Assert.True(BackgroundServiceWatchdog.IsRecoverable(new InvalidOperationException("x")));
        Assert.True(BackgroundServiceWatchdog.IsRecoverable(new IOException("x")));
        Assert.True(BackgroundServiceWatchdog.IsRecoverable(new UnauthorizedAccessException("x")));
        Assert.True(BackgroundServiceWatchdog.IsRecoverable(new NullReferenceException("x")));
    }

    [Fact]
    public void IsRecoverable_OnOutOfMemoryException_ReturnsFalse()
    {
        Assert.False(BackgroundServiceWatchdog.IsRecoverable(new OutOfMemoryException()));
    }

    [Fact]
    public void IsRecoverable_OnOperationCanceledException_ReturnsFalse()
    {
        Assert.False(BackgroundServiceWatchdog.IsRecoverable(new OperationCanceledException()));
        Assert.False(BackgroundServiceWatchdog.IsRecoverable(new TaskCanceledException()));
    }

    [Fact]
    public void IsRecoverable_OnAccessViolationException_ReturnsFalse()
    {
        Assert.False(BackgroundServiceWatchdog.IsRecoverable(new AccessViolationException()));
    }

    [Fact]
    public void IsRecoverable_OnNull_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => BackgroundServiceWatchdog.IsRecoverable(null!));
    }
}
