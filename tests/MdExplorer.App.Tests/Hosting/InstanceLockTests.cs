using System.IO;
using System.Threading;
using MdExplorer.App.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Hosting;

/// <summary>
/// Solange die erste Instanz lebt, ist jede weitere die zweite; danach ist der Name wieder frei.
/// </summary>
public sealed class InstanceLockTests
{
    private static readonly ILogger Logger = NullLogger.Instance;

    private readonly string _name = "MdExplorer-Test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void Acquire_FirstTime_IsTheFirstInstance()
    {
        using InstanceLock first = InstanceLock.Acquire(_name, Logger);

        Assert.True(first.IsFirstInstance);
    }

    [Fact]
    public void Acquire_WhileTheFirstLives_IsNotTheFirstInstance()
    {
        using InstanceLock first = InstanceLock.Acquire(_name, Logger);
        using InstanceLock second = InstanceLock.Acquire(_name, Logger);

        Assert.False(second.IsFirstInstance);
    }

    [Fact]
    public async Task Acquire_FromAnotherThread_IsStillNotTheFirstInstance()
    {
        // Ein Mutex-Besitz hinge am Thread; die Sperre fragt deshalb nur, ob der Name neu ist.
        using InstanceLock first = InstanceLock.Acquire(_name, Logger);

        bool secondIsFirst = await Task.Run(
            () =>
            {
                using InstanceLock second = InstanceLock.Acquire(_name, Logger);
                return second.IsFirstInstance;
            },
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(secondIsFirst);
    }

    [Fact]
    public void Acquire_AfterTheFirstEnded_IsTheFirstInstanceAgain()
    {
        InstanceLock first = InstanceLock.Acquire(_name, Logger);
        first.Dispose();

        using InstanceLock next = InstanceLock.Acquire(_name, Logger);

        Assert.True(next.IsFirstInstance);
    }

    [Fact]
    public void Acquire_OwnLock_IsExclusive()
    {
        using InstanceLock first = InstanceLock.Acquire(_name, Logger);

        Assert.True(first.IsExclusive);
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(WaitHandleCannotBeOpenedException))]
    [InlineData(typeof(IOException))]
    public void Acquire_WhenTheNameBelongsToSomeoneElse_StartsWithoutTheLock(Type failure)
    {
        // Hat ein anderer Benutzer oder eine Instanz mit erhöhten Rechten den Namen angelegt,
        // scheitert das Öffnen. Das darf den Start nicht verhindern.
        using InstanceLock instanceLock = InstanceLock.Acquire(
            _name,
            _ => throw (Exception)Activator.CreateInstance(failure)!,
            Logger);

        Assert.True(instanceLock.IsFirstInstance);
        Assert.False(instanceLock.IsExclusive);
    }

    [Fact]
    public void Acquire_WithAnUnexpectedFailure_LetsItThrough()
    {
        _ = Assert.Throws<InvalidOperationException>(() => InstanceLock.Acquire(_name, _ => throw new InvalidOperationException("kaputt"), Logger));
    }

    [Fact]
    public void Acquire_WithoutName_Throws()
    {
        _ = Assert.Throws<ArgumentException>(() => InstanceLock.Acquire(" ", Logger));
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
