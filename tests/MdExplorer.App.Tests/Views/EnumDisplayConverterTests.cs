using MdExplorer.App.Converters;
using MdExplorer.App.Services;
using MdExplorer.Core.Models;
using MdExplorer.Search.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Auswahlfelder und Protokollstufen zeigen Anzeigetexte, nicht die Bezeichner aus dem Code.
/// </summary>
public sealed class EnumDisplayConverterTests
{
    public static TheoryData<object> ShownValues =>
    [
        .. Enum.GetValues<SearchMode>().Cast<object>(),
        .. Enum.GetValues<SimilarityMode>().Cast<object>(),
        .. Enum.GetValues<AppTheme>().Cast<object>(),
        .. Enum.GetValues<OperationHealth>().Cast<object>(),
        .. Enum.GetValues<LogLevel>().Where(level => level != LogLevel.None).Cast<object>(),
    ];

    [Theory]
    [MemberData(nameof(ShownValues))]
    public void Display_EveryShownValue_HasItsOwnText(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string text = EnumDisplayConverter.Display(value);

        Assert.NotEqual(value.ToString(), text);
        Assert.DoesNotContain("[?", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Display_Nothing_IsEmpty() => Assert.Empty(EnumDisplayConverter.Display(null));
}
