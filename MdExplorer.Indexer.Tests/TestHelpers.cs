using Microsoft.Extensions.Options;

namespace MdExplorer.Indexer.Tests;

/// <summary>
/// Hilfsmethoden für Indexer-Tests. <c>OptionsWrapper&lt;T&gt;</c> wird direkt verwendet,
/// um die Namensraumkollision zwischen <c>MdExplorer.Indexer.Options</c> und
/// <c>Microsoft.Extensions.Options.Options</c> zu vermeiden.
/// </summary>
internal static class TestHelpers
{
    public static IOptions<T> ToOptions<T>(this T value)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        return new OptionsWrapper<T>(value);
    }
}
