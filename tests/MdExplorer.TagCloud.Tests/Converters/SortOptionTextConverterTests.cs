using System.Globalization;
using MdExplorer.TagCloud.Converters;
using MdExplorer.TagCloud.Models;

namespace MdExplorer.TagCloud.Tests.Converters;

/// <summary>
/// Jede Sortierung hat einen eigenen Anzeigetext; ein neuer Enum-Wert ohne Eintrag in der
/// Ressourcendatei zeigte sonst seinen englischen Namen oder den Schlüssel.
/// </summary>
public sealed class SortOptionTextConverterTests
{
    public static TheoryData<TagCloudSortOption> AllOptions => [.. Enum.GetValues<TagCloudSortOption>()];

    [Theory]
    [MemberData(nameof(AllOptions))]
    public void Convert_OnEachOption_ReturnsItsOwnResourceText(TagCloudSortOption option)
    {
        SortOptionTextConverter sut = new();

        object text = sut.Convert(option, typeof(string), parameter: null, CultureInfo.InvariantCulture);

        string shown = Assert.IsType<string>(text);
        Assert.False(string.IsNullOrWhiteSpace(shown));
        Assert.NotEqual(option.ToString(), shown);
        Assert.DoesNotContain("TagCloud_", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void TextOf_ForAllOptions_GivesDistinctTexts()
    {
        TagCloudSortOption[] options = Enum.GetValues<TagCloudSortOption>();

        Assert.Equal(options.Length, options.Select(SortOptionTextConverter.TextOf).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Convert_OnSomethingElse_ReturnsAnEmptyText()
    {
        Assert.Equal(string.Empty, new SortOptionTextConverter().Convert("x", typeof(string), parameter: null, CultureInfo.InvariantCulture));
    }
}
