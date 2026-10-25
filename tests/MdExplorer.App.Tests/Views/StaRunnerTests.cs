namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Was auf dem STA-Thread schiefgeht, erscheint als roter Test. Eine Ausnahme, die dort niemand
/// weiterreicht, risse den ganzen Testlauf ab.
/// </summary>
public sealed class StaRunnerTests
{
    [Fact]
    public void Run_AnyFailure_ReachesTheCaller()
        => Assert.Throws<ArgumentException>(() => StaRunner.Run(() => throw new ArgumentException("Fehler auf dem STA-Thread")));

    [Fact]
    public void Run_RunsOnAnStaThread()
    {
        ApartmentState state = ApartmentState.Unknown;

        StaRunner.Run(() => state = Thread.CurrentThread.GetApartmentState());

        Assert.Equal(ApartmentState.STA, state);
    }
}
