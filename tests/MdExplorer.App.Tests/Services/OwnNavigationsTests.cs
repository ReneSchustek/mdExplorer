using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Die Vorschau erkennt ihr eigenes Dokument an der Vormerkung vor dem Laden, nicht daran, ob
/// WebView2 die Navigation als Klick meldet.
/// </summary>
public sealed class OwnNavigationsTests
{
    private readonly OwnNavigations _sut = new();

    [Theory]
    [InlineData("about:blank")]
    [InlineData("data:text/html;charset=utf-8;base64,PGgxPg==")]
    public void TryConsume_AfterExpect_RecognizesTheOwnDocumentOnce(string address)
    {
        _sut.Expect();

        Assert.True(_sut.TryConsume(address));
        Assert.False(_sut.TryConsume(address));
    }

    [Fact]
    public void TryConsume_WithoutExpect_IsNotTheOwnDocument()
    {
        // Etwa eine Weiterleitung im Dokument auf about:blank: Die gehört nicht der Anwendung.
        Assert.False(_sut.TryConsume("about:blank"));
    }

    [Fact]
    public void TryConsume_ForAnotherTarget_KeepsTheExpectation()
    {
        _sut.Expect();

        Assert.False(_sut.TryConsume("https://example.org/"));
        Assert.True(_sut.TryConsume("about:blank"));
    }

    [Fact]
    public void TryConsume_AfterTwoLoadsInARow_RecognizesBoth()
    {
        // Zwei rasch aufeinander folgende Ladevorgänge lösen zwei Navigationen aus.
        _sut.Expect();
        _sut.Expect();

        Assert.True(_sut.TryConsume("about:blank"));
        Assert.True(_sut.TryConsume("about:blank"));
        Assert.False(_sut.TryConsume("about:blank"));
    }

    [Fact]
    public void Classify_WithTheExpectation_KeepsTheOwnDocumentEvenAsAClick()
    {
        // Meldet WebView2 die eigene Navigation als vom Nutzer ausgelöst, bliebe die Vorschau
        // ohne Vormerkung leer.
        _sut.Expect();

        Assert.Equal(PreviewNavigationKind.Document, PreviewNavigationPolicy.Classify("about:blank", isUserInitiated: true, _sut.TryConsume));
        Assert.Equal(PreviewNavigationKind.Blocked, PreviewNavigationPolicy.Classify("about:blank", isUserInitiated: false, _sut.TryConsume));
    }

    [Fact]
    public void TryConsume_WithoutUri_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => _sut.TryConsume(null!));
    }
}
