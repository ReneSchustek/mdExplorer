using MdExplorer.Core.Abstractions;

namespace MdExplorer.Parser.Tests.Fakes;

internal sealed class FakeMarkdownSourceProvider : IMarkdownSourceProvider
{
    public List<MarkdownSourceSnapshot> Sources { get; } = [];

    /// <summary>Optionaler Throw fuer Defense-in-Depth-Tests.</summary>
    public Exception? ThrowOnNextEnumeration { get; set; }

    public async IAsyncEnumerable<MarkdownSourceSnapshot> EnumerateAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Exception? toThrow = ThrowOnNextEnumeration;
        if (toThrow is not null)
        {
            ThrowOnNextEnumeration = null;
            await Task.Yield();
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(toThrow).Throw();
        }
        foreach (MarkdownSourceSnapshot snapshot in Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return snapshot;
            await Task.Yield();
        }
    }
}
