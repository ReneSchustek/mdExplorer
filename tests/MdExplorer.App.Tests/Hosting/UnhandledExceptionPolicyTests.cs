using MdExplorer.App.Hosting;

namespace MdExplorer.App.Tests.Hosting;

/// <summary>
/// Eine Ausnahme, die bis zum Dispatcher durchfällt: Vor dem Ende des Starts beendet sie die
/// Anwendung, danach zeigt sie einen Dialog, aber nicht für jede Wiederholung einen neuen.
/// </summary>
public sealed class UnhandledExceptionPolicyTests
{
    [Fact]
    public void Decide_BeforeStartupCompleted_AbortsStartup()
    {
        UnhandledExceptionPolicy sut = new();

        Assert.Equal(UnhandledExceptionAction.AbortStartup, sut.Decide(new InvalidCastException("Startfehler")));
    }

    [Fact]
    public void Decide_SecondFailureWhileStartupIsAborting_OnlyLogs()
    {
        UnhandledExceptionPolicy sut = new();
        _ = sut.Decide(new InvalidCastException("erster"));

        Assert.Equal(UnhandledExceptionAction.LogOnly, sut.Decide(new FormatException("zweiter")));
    }

    [Fact]
    public void Decide_AfterStartup_ShowsDialog()
    {
        UnhandledExceptionPolicy sut = Started();

        Assert.Equal(UnhandledExceptionAction.ShowDialog, sut.Decide(new InvalidCastException("Laufzeitfehler")));
    }

    [Fact]
    public void Decide_WhileADialogIsOpen_OnlyLogs()
    {
        UnhandledExceptionPolicy sut = Started();
        _ = sut.Decide(new InvalidCastException("erster"));

        Assert.Equal(UnhandledExceptionAction.LogOnly, sut.Decide(new FormatException("zweiter")));
    }

    [Fact]
    public void Decide_SameFailureAfterTheDialogClosed_OnlyLogs()
    {
        UnhandledExceptionPolicy sut = Started();
        _ = sut.Decide(new InvalidCastException("Layout"));
        sut.DialogClosed();

        Assert.Equal(UnhandledExceptionAction.LogOnly, sut.Decide(new InvalidCastException("Layout")));
    }

    [Fact]
    public void Decide_OtherFailureAfterTheDialogClosed_ShowsDialog()
    {
        UnhandledExceptionPolicy sut = Started();
        _ = sut.Decide(new InvalidCastException("Layout"));
        sut.DialogClosed();

        Assert.Equal(UnhandledExceptionAction.ShowDialog, sut.Decide(new InvalidCastException("Zeitgeber")));
    }

    [Fact]
    public void Decide_WithoutException_Throws()
        => Assert.Throws<ArgumentNullException>(() => new UnhandledExceptionPolicy().Decide(null!));

    private static UnhandledExceptionPolicy Started()
    {
        UnhandledExceptionPolicy policy = new();
        policy.MarkStartupCompleted();
        return policy;
    }
}
