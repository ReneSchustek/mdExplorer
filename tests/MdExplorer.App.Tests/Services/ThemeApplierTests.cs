using System.Collections.ObjectModel;
using System.Windows;
using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Tests für den <see cref="ThemeApplier"/>.
/// </summary>
/// <remarks>
/// Geprüft werden Auswahl und Austausch, nicht das Auflösen der Ressourcenadressen —
/// letzteres geht nur in einer laufenden Anwendung. Ein zweiter Eintrag statt eines
/// Austauschs wäre der teure Fehler: Er ließe die alte Belegung stehen, und welche
/// gewinnt, hinge an der Reihenfolge.
/// </remarks>
public sealed class ThemeApplierTests
{
    private const string PaletteMarkerKey = "AppBackgroundBrush";

    [Theory]
    [InlineData(false, "Light.xaml")]
    [InlineData(true, "Dark.xaml")]
    public void Apply_ChoosesPaletteFromEffectiveTheme(bool isDarkMode, string expectedFile)
    {
        RecordingLoader loader = new();
        ThemeApplier sut = new(new FakeEffectiveThemeProvider(isDarkMode), loader.Load);
        Collection<ResourceDictionary> dictionaries = [];

        sut.Apply(dictionaries);

        Assert.EndsWith(expectedFile, loader.LastUri!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_OnEmptyDictionaries_AddsPalette()
    {
        RecordingLoader loader = new();
        ThemeApplier sut = new(new FakeEffectiveThemeProvider(isDarkMode: false), loader.Load);
        Collection<ResourceDictionary> dictionaries = [];

        sut.Apply(dictionaries);

        _ = Assert.Single(dictionaries);
    }

    [Fact]
    public void Apply_OnRepeatedCalls_ReplacesInsteadOfAppending()
    {
        RecordingLoader loader = new();
        ThemeApplier sut = new(new FakeEffectiveThemeProvider(isDarkMode: false), loader.Load);
        Collection<ResourceDictionary> dictionaries = [];

        sut.Apply(dictionaries);
        sut.Apply(dictionaries);
        sut.Apply(dictionaries);

        _ = Assert.Single(dictionaries);
    }

    [Fact]
    public void Apply_KeepsPositionOfPaletteAmongOtherDictionaries()
    {
        RecordingLoader loader = new();
        ThemeApplier sut = new(new FakeEffectiveThemeProvider(isDarkMode: true), loader.Load);
        ResourceDictionary tokens = new() { { "SpacingS", 8d } };
        ResourceDictionary later = new() { { "SomethingElse", "x" } };
        Collection<ResourceDictionary> dictionaries = [tokens, PaletteStandIn("vorher"), later];

        sut.Apply(dictionaries);

        Assert.Equal(3, dictionaries.Count);
        Assert.Same(tokens, dictionaries[0]);
        Assert.Same(later, dictionaries[2]);
        Assert.Equal("neu", dictionaries[1][PaletteMarkerKey]);
    }

    [Fact]
    public void Apply_OnNullDictionaries_Throws()
    {
        ThemeApplier sut = new(new FakeEffectiveThemeProvider(isDarkMode: false), new RecordingLoader().Load);

        _ = Assert.Throws<ArgumentNullException>(() => sut.Apply(null!));
    }

    [Fact]
    public void Constructor_OnMissingProvider_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new ThemeApplier(null!));
    }

    [Fact]
    public void Constructor_OnMissingLoader_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(
            () => new ThemeApplier(new FakeEffectiveThemeProvider(isDarkMode: false), null!));
    }

    /// <summary>
    /// Steht für eine bereits geladene Belegung — erkennbar an ihrem Merkmalsschlüssel.
    /// </summary>
    private static ResourceDictionary PaletteStandIn(string marker) => new() { { PaletteMarkerKey, marker } };

    /// <summary>
    /// Merkt sich die angeforderte Adresse und liefert eine Belegung ohne Dateizugriff.
    /// </summary>
    private sealed class RecordingLoader
    {
        public Uri? LastUri { get; private set; }

        public ResourceDictionary Load(Uri uri)
        {
            LastUri = uri;
            return PaletteStandIn("neu");
        }
    }

    private sealed class FakeEffectiveThemeProvider(bool isDarkMode) : IEffectiveThemeProvider
    {
        public bool IsDarkMode { get; } = isDarkMode;
    }
}
