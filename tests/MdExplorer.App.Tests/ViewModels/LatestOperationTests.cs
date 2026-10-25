using MdExplorer.App.ViewModels;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Von mehreren gleichartigen Ladevorgängen zählt nur der jüngste. Klickt der Nutzer schnell
/// hintereinander zwei Dateien an, darf die Antwort der ersten nicht nach der zweiten eintreffen
/// und die Anzeige überschreiben.
/// </summary>
public sealed class LatestOperationTests
{
    [Fact]
    public void Begin_WhileAnEarlierOperationRuns_CancelsTheEarlierToken()
    {
        using LatestOperation sut = new();
        using LatestOperation.Ticket first = sut.Begin(CancellationToken.None);

        using LatestOperation.Ticket second = sut.Begin(CancellationToken.None);

        Assert.True(first.Token.IsCancellationRequested);
        Assert.False(second.Token.IsCancellationRequested);
    }

    [Fact]
    public void IsCurrent_AfterANewerBegin_IsFalseOnlyForTheOlderTicket()
    {
        using LatestOperation sut = new();
        using LatestOperation.Ticket first = sut.Begin(CancellationToken.None);
        using LatestOperation.Ticket second = sut.Begin(CancellationToken.None);

        Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent);
    }

    [Fact]
    public void Begin_WhenTheOuterTokenCancels_CancelsTheTicket()
    {
        using CancellationTokenSource outer = new();
        using LatestOperation sut = new();
        using LatestOperation.Ticket ticket = sut.Begin(outer.Token);

        outer.Cancel();

        Assert.True(ticket.Token.IsCancellationRequested);
    }

    [Fact]
    public void Dispose_CancelsTheRunningTicket()
    {
        LatestOperation sut = new();
        using LatestOperation.Ticket ticket = sut.Begin(CancellationToken.None);

        sut.Dispose();

        Assert.True(ticket.Token.IsCancellationRequested);
        Assert.False(ticket.IsCurrent);
    }
}
