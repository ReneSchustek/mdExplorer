using System.ComponentModel;
using MdExplorer.App.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Hosting;

/// <summary>
/// Das Hauptfenster schließt erst, wenn die Hintergrunddienste gestoppt sind. In <c>OnExit</c>
/// wartete WPF auf kein Ende des Stoppens; Dienste, Container und Protokoll endeten dort halb.
/// </summary>
public sealed class HostShutdownTests
{
    [Fact]
    public void HoldClosing_FirstRequest_CancelsAndAsksForTheStop()
    {
        HostShutdown sut = new(() => Task.CompletedTask, NullLogger<HostShutdown>.Instance);
        CancelEventArgs args = new();

        bool startStop = sut.HoldClosing(args);

        Assert.True(startStop);
        Assert.True(args.Cancel);
    }

    [Fact]
    public void HoldClosing_SecondRequestWhileStopping_CancelsWithoutAnotherStop()
    {
        HostShutdown sut = new(() => Task.CompletedTask, NullLogger<HostShutdown>.Instance);
        _ = sut.HoldClosing(new CancelEventArgs());
        CancelEventArgs second = new();

        bool startStop = sut.HoldClosing(second);

        Assert.False(startStop);
        Assert.True(second.Cancel);
    }

    [Fact]
    public void HoldClosing_AlreadyCancelledByAnotherHandler_LeavesItAlone()
    {
        // Hält schon die Rückfrage zu ungespeicherten Änderungen das Fenster offen, darf das
        // Stoppen nicht beginnen: Die Anwendung liefe sonst ohne Dienste weiter.
        HostShutdown sut = new(() => Task.CompletedTask, NullLogger<HostShutdown>.Instance);
        CancelEventArgs args = new() { Cancel = true };

        Assert.False(sut.HoldClosing(args));
        Assert.False(sut.IsCompleted);
    }

    [Fact]
    public async Task HoldClosing_AfterTheStop_LetsTheWindowClose()
    {
        HostShutdown sut = new(() => Task.CompletedTask, NullLogger<HostShutdown>.Instance);
        _ = sut.HoldClosing(new CancelEventArgs());
        await sut.StopAsync();
        CancelEventArgs args = new();

        Assert.False(sut.HoldClosing(args));
        Assert.False(args.Cancel);
    }

    [Fact]
    public async Task StopAsync_WhenAServiceFails_CompletesAnyway()
    {
        HostShutdown sut = new(
            () => Task.FromException(new InvalidOperationException("Dienst hängt")),
            NullLogger<HostShutdown>.Instance);

        await sut.StopAsync();

        Assert.True(sut.IsCompleted);
    }

    [Fact]
    public async Task StopAsync_WhenTheStopIsCancelled_CompletesAnyway()
    {
        HostShutdown sut = new(() => Task.FromCanceled(new CancellationToken(canceled: true)), NullLogger<HostShutdown>.Instance);

        await sut.StopAsync();

        Assert.True(sut.IsCompleted);
    }
}
