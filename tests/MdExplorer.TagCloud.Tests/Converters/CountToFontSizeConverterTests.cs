using System.Globalization;
using MdExplorer.TagCloud.Converters;

namespace MdExplorer.TagCloud.Tests.Converters;

/// <summary>Verifiziert den MultiValue-Konverter, der im XAML der Tag-Cloud verwendet wird.</summary>
public sealed class CountToFontSizeConverterTests
{
    private static readonly CountToFontSizeConverter Converter = new()
    {
        MinFontSize = 10.0,
        MaxFontSize = 26.0,
    };

    [Fact]
    public void CountToFontSizeConverter_OnMinCount_ReturnsMinSize()
    {
        object result = Converter.Convert([1, 1, 100], typeof(double), parameter: null, CultureInfo.InvariantCulture);

        double size = Assert.IsType<double>(result);
        Assert.Equal(10.0, size, precision: 6);
    }

    [Fact]
    public void CountToFontSizeConverter_OnMaxCount_ReturnsMaxSize()
    {
        object result = Converter.Convert([100, 1, 100], typeof(double), parameter: null, CultureInfo.InvariantCulture);

        double size = Assert.IsType<double>(result);
        Assert.Equal(26.0, size, precision: 6);
    }

    [Fact]
    public void Convert_OnInvalidInputs_FallsBackToMinSize()
    {
        object result = Converter.Convert(["nope", 1, 100], typeof(double), parameter: null, CultureInfo.InvariantCulture);

        Assert.Equal(10.0, Assert.IsType<double>(result), precision: 6);
    }

    [Fact]
    public void ConvertBack_Throws_NotSupportedException()
    {
        _ = Assert.Throws<NotSupportedException>(() =>
            Converter.ConvertBack(16.0, [typeof(int), typeof(int), typeof(int)], parameter: null, CultureInfo.InvariantCulture));
    }
}
