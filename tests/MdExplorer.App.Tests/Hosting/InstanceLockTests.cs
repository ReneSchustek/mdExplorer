using MdExplorer.App.Hosting;

namespace MdExplorer.App.Tests.Hosting;

/// <summary>
/// Solange die erste Instanz lebt, ist jede weitere die zweite; danach ist der Name wieder frei.
/// </summary>
public sealed class InstanceLockTests
{
    private readonly string _name = "MdExplorer-Test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void Acquire_FirstTime_IsTheFirstInstance()
    {
        using InstanceLock first = InstanceLock.Acquire(_name);

        Assert.True(first.IsFirstInstance);
    }

    [Fact]
    public void Acquire_WhileTheFirstLives_IsNotTheFirstInstance()
    {
        using InstanceLock first = InstanceLock.Acquire(_name);
        using InstanceLock second = InstanceLock.Acquire(_name);

        Assert.False(second.IsFirstInstance);
    }

    [Fact]
    public async Task Acquire_FromAnotherThread_IsStillNotTheFirstInstance()
    {
        // Ein Mutex-Besitz hinge am Thread; die Sperre fragt deshalb nur, ob der Name neu ist.
        using InstanceLock first = InstanceLock.Acquire(_name);

        bool secondIsFirst = await Task.Run(
            () =>
            {
                using InstanceLock second = InstanceLock.Acquire(_name);
                return second.IsFirstInstance;
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(secondIsFirst);
    }

    [Fact]
    public void Acquire_AfterTheFirstEnded_IsTheFirstInstanceAgain()
    {
        InstanceLock first = InstanceLock.Acquire(_name);
        first.Dispose();

        using InstanceLock next = InstanceLock.Acquire(_name);

        Assert.True(next.IsFirstInstance);
    }

    [Fact]
    public void Acquire_WithoutName_Throws()
    {
        _ = Assert.Throws<ArgumentException>(() => InstanceLock.Acquire(" "));
    }

    [Fact]
    public void Identity_ForUser_SeparatesUsers()
    {
        InstanceIdentity first = InstanceIdentity.ForUser("S-1-5-21-1");
        InstanceIdentity second = InstanceIdentity.ForUser("S-1-5-21-2");

        Assert.NotEqual(first.LockName, second.LockName);
        Assert.NotEqual(first.PipeName, second.PipeName);
        Assert.StartsWith("MdExplorer-", first.LockName, StringComparison.Ordinal);
        // Der Namensraum (Global\ oder Local\) wählt die Sperre selbst.
        Assert.DoesNotContain('\\', first.LockName);
    }

    [Fact]
    public void Identity_ForCurrentUser_IsStable()
    {
        Assert.Equal(InstanceIdentity.ForCurrentUser(), InstanceIdentity.ForCurrentUser());
    }

    [Fact]
    public void Identity_ForUser_WithoutKey_Throws()
    {
        _ = Assert.Throws<ArgumentException>(() => InstanceIdentity.ForUser(""));
    }
}
