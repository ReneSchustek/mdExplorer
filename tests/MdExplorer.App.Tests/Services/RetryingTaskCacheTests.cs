using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Ein gelungener Wert wird geteilt, ein gescheiterter Versuch nicht: Der nächste Aufruf versucht
/// es erneut.
/// </summary>
public sealed class RetryingTaskCacheTests
{
    [Fact]
    public async Task GetAsync_AfterSuccess_ReturnsTheSameValueWithoutCreatingAgain()
    {
        int created = 0;
        RetryingTaskCache<object> sut = new(() =>
        {
            created++;
            return Task.FromResult(new object());
        });

        object first = await sut.GetAsync().ConfigureAwait(true);
        object second = await sut.GetAsync().ConfigureAwait(true);

        Assert.Same(first, second);
        Assert.Equal(1, created);
    }

    /// <remarks>
    /// Fehlt die Browser-Laufzeit beim ersten Öffnen und wird danach installiert, muss das nächste
    /// Öffnen sie finden, ohne dass die Anwendung neu startet.
    /// </remarks>
    [Fact]
    public async Task GetAsync_AfterAFailure_TriesAgain()
    {
        int created = 0;
        RetryingTaskCache<string> sut = new(() =>
        {
            created++;
            return created == 1
                ? Task.FromException<string>(new InvalidOperationException("Laufzeit fehlt"))
                : Task.FromResult("bereit");
        });

        _ = await Assert.ThrowsAsync<InvalidOperationException>(sut.GetAsync).ConfigureAwait(true);
        string second = await sut.GetAsync().ConfigureAwait(true);

        Assert.Equal("bereit", second);
        Assert.Equal(2, created);
    }

    [Fact]
    public void GetAsync_WhileACreationRuns_SharesIt()
    {
        int created = 0;
        TaskCompletionSource<string> pending = new();
        RetryingTaskCache<string> sut = new(() =>
        {
            created++;
            return pending.Task;
        });

        Task<string> first = sut.GetAsync();
        Task<string> second = sut.GetAsync();

        Assert.Same(first, second);
        Assert.Equal(1, created);
    }
}
