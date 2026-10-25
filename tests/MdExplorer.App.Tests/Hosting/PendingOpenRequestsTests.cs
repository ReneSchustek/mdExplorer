using MdExplorer.App.Hosting;
using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Hosting;

/// <summary>
/// Was vor dem Hauptfenster ankommt, wartet; was danach ankommt, geht sofort durch.
/// </summary>
public sealed class PendingOpenRequestsTests
{
    private static readonly MarkdownPathCheck First = MarkdownPathCheck.Accepted(@"C:\a.md");
    private static readonly MarkdownPathCheck Second = MarkdownPathCheck.Accepted(@"C:\b.md");

    private readonly PendingOpenRequests _sut = new();
    private readonly List<MarkdownPathCheck> _handled = [];

    [Fact]
    public void Attach_HandsOverWhatArrivedBefore()
    {
        _sut.Post(First);

        _sut.Attach(_handled.Add);

        Assert.Equal([First], _handled);
    }

    [Fact]
    public void Attach_WithNothingPending_HandsOverNothing()
    {
        _sut.Attach(_handled.Add);

        Assert.Empty(_handled);
    }

    [Fact]
    public void Post_BeforeAttach_KeepsOnlyTheLatestFile()
    {
        // Der Dokumentbereich zeigt eine Datei; die zuletzt doppelt geklickte gewinnt.
        _sut.Post(First);
        _sut.Post(Second);

        _sut.Attach(_handled.Add);

        Assert.Equal([Second], _handled);
    }

    [Fact]
    public void Post_BeforeAttach_AWindowOnlyRequestDoesNotDisplaceAFile()
    {
        _sut.Post(First);
        _sut.Post(MarkdownPathCheck.None);

        _sut.Attach(_handled.Add);

        Assert.Equal([First], _handled);
    }

    [Fact]
    public void Post_BeforeAttach_KeepsAWindowOnlyRequest()
    {
        _sut.Post(MarkdownPathCheck.None);

        _sut.Attach(_handled.Add);

        Assert.Equal([MarkdownPathCheck.None], _handled);
    }

    [Fact]
    public void Post_BeforeAttach_KeepsARejectionSoItIsReported()
    {
        MarkdownPathCheck rejected = MarkdownPathCheck.Rejected(@"C:\a.txt", MarkdownPathProblem.UnsupportedExtension);
        _sut.Post(rejected);

        _sut.Attach(_handled.Add);

        Assert.Equal([rejected], _handled);
    }

    [Fact]
    public void Post_AfterAttach_GoesStraightThrough()
    {
        _sut.Attach(_handled.Add);

        _sut.Post(First);
        _sut.Post(MarkdownPathCheck.None);

        Assert.Equal([First, MarkdownPathCheck.None], _handled);
    }

    [Fact]
    public void Attach_Twice_Throws()
    {
        _sut.Attach(_handled.Add);

        _ = Assert.Throws<InvalidOperationException>(() => _sut.Attach(_handled.Add));
    }

    [Fact]
    public async Task Post_FromManyThreadsAroundAttach_LosesNothingAfterAttach()
    {
        List<MarkdownPathCheck> handled = [];
        Lock gate = new();
        MarkdownPathCheck[] requests = [.. Enumerable.Range(0, 200).Select(i => MarkdownPathCheck.Accepted($@"C:\{i}.md"))];

        Task posting = Task.Run(() => Parallel.ForEach(requests, _sut.Post), TestContext.Current.CancellationToken);
        _sut.Attach(request =>
        {
            lock (gate)
            {
                handled.Add(request);
            }
        });
        await posting.ConfigureAwait(true);

        // Vor dem Verbinden zählt nur der jüngste; danach geht jeder einzeln durch. Mehr als alle
        // kann es nicht sein, und wenigstens einer kommt immer an.
        Assert.InRange(handled.Count, 1, requests.Length);
        Assert.All(handled, request => Assert.Contains(request, requests));
    }
}
