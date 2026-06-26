using MdExplorer.TagCloud.Services;

namespace MdExplorer.TagCloud.Tests.Services;

/// <summary>
/// Reine Unit-Tests des logarithmischen Skalen-Rechners. Keine Allokationen, kein WPF.
/// </summary>
public sealed class TagFontSizeCalculatorTests
{
    [Fact]
    public void Compute_OnMinCount_ReturnsMinSize()
    {
        double size = TagFontSizeCalculator.Compute(count: 1, minCount: 1, maxCount: 100, minFontSize: 10.0, maxFontSize: 26.0);

        Assert.Equal(10.0, size, precision: 6);
    }

    [Fact]
    public void Compute_OnMaxCount_ReturnsMaxSize()
    {
        double size = TagFontSizeCalculator.Compute(count: 100, minCount: 1, maxCount: 100, minFontSize: 10.0, maxFontSize: 26.0);

        Assert.Equal(26.0, size, precision: 6);
    }

    [Fact]
    public void Compute_OnMidLogCount_ReturnsLinearMidpoint()
    {
        // log10(10) = 1, log10(100) = 2 → normalisiert 0,5 → erwarteter Wert genau in der Mitte.
        double size = TagFontSizeCalculator.Compute(count: 10, minCount: 1, maxCount: 100, minFontSize: 10.0, maxFontSize: 26.0);

        Assert.Equal(18.0, size, precision: 6);
    }

    [Fact]
    public void Compute_OnEqualMinMax_ReturnsMinSize()
    {
        double size = TagFontSizeCalculator.Compute(count: 5, minCount: 5, maxCount: 5, minFontSize: 10.0, maxFontSize: 26.0);

        Assert.Equal(10.0, size, precision: 6);
    }

    [Fact]
    public void Compute_OnCountBelowMin_ClampsToMinSize()
    {
        double size = TagFontSizeCalculator.Compute(count: 0, minCount: 1, maxCount: 100, minFontSize: 10.0, maxFontSize: 26.0);

        Assert.Equal(10.0, size, precision: 6);
    }

    [Fact]
    public void Compute_OnCountAboveMax_ClampsToMaxSize()
    {
        double size = TagFontSizeCalculator.Compute(count: 500, minCount: 1, maxCount: 100, minFontSize: 10.0, maxFontSize: 26.0);

        Assert.Equal(26.0, size, precision: 6);
    }
}
