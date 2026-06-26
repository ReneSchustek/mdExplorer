using MdExplorer.Parser.Services;

namespace MdExplorer.Parser.Tests.Services;

public sealed class TagNormalizerTests
{
    private readonly TagNormalizer _sut = new();

    [Theory]
    [InlineData("Foo", "foo")]
    [InlineData("FOO", "foo")]
    [InlineData("Bar Baz", "bar-baz")]
    [InlineData("  leading and trailing  ", "leading-and-trailing")]
    [InlineData("multiple   spaces", "multiple-spaces")]
    [InlineData("snake_case", "snake-case")]
    [InlineData("kebab-already", "kebab-already")]
    [InlineData("dots.and,commas", "dotsandcommas")]
    [InlineData("MünchenÖlÜbung", "münchenölübung")]
    [InlineData("Café-Straße", "café-straße")]
    [InlineData("Tag42", "tag42")]
    [InlineData("---Foo---", "foo")]
    public void ToSlug_NormalizesAsExpected(string input, string expected)
    {
        string actual = _sut.ToSlug(input);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ToSlug_CollapsesMixedSeparators()
    {
        string actual = _sut.ToSlug("foo  __  bar");

        Assert.Equal("foo-bar", actual);
    }

    [Fact]
    public void ToSlug_OnNull_ThrowsArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(() => _sut.ToSlug(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ToSlug_OnEmptyOrWhitespace_ThrowsArgumentException(string input)
    {
        _ = Assert.Throws<ArgumentException>(() => _sut.ToSlug(input));
    }

    [Fact]
    public void ToSlug_OnPureSymbols_Throws()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => _sut.ToSlug("...,,,"));

        Assert.Contains("slug-tauglichen", exception.Message, StringComparison.Ordinal);
    }
}
