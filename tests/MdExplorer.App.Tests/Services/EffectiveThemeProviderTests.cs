using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.Core.Models;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Die Wahl in den Einstellungen geht vor der von Windows; nur „System" folgt Windows.
/// </summary>
/// <remarks>
/// Oberfläche und Vorschau fragen dieselbe Regel. Fragte die Vorschau nur Windows, bekäme,
/// wer „Dunkel" wählt, während Windows hell steht, eine weiße Fläche mitten in einer
/// dunklen Anwendung.
/// </remarks>
public sealed class EffectiveThemeProviderTests
{
    [Theory]
    [InlineData(AppTheme.Light, false, false)]
    [InlineData(AppTheme.Light, true, false)]
    [InlineData(AppTheme.Dark, false, true)]
    [InlineData(AppTheme.Dark, true, true)]
    public void ChosenTheme_BeatsTheSystem(AppTheme chosen, bool systemIsDark, bool expected)
    {
        EffectiveThemeProvider sut = Create(chosen, systemIsDark);

        Assert.Equal(expected, sut.IsDarkMode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnSystemChoice_FollowsWindows(bool systemIsDark)
    {
        EffectiveThemeProvider sut = Create(AppTheme.System, systemIsDark);

        Assert.Equal(systemIsDark, sut.IsDarkMode);
    }

    [Fact]
    public async Task IsDarkMode_AfterTheSettingChanges_FollowsWithoutBeingRecreated()
    {
        FakeSettingsService settings = new(SettingsWith(AppTheme.Light));
        EffectiveThemeProvider sut = new(settings, new FakeSystemTheme(isDarkMode: false));
        Assert.False(sut.IsDarkMode);

        // Die Einstellung ist die Quelle, nicht ein beim Erzeugen abgelesener Wert:
        // Sonst bliebe die Vorschau nach dem Umschalten in der alten Belegung.
        await settings.SaveAsync(SettingsWith(AppTheme.Dark), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(sut.IsDarkMode);
    }

    [Fact]
    public void Constructor_OnMissingDependencies_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(
            () => new EffectiveThemeProvider(null!, new FakeSystemTheme(isDarkMode: false)));
        _ = Assert.Throws<ArgumentNullException>(
            () => new EffectiveThemeProvider(new FakeSettingsService(SettingsWith(AppTheme.System)), null!));
    }

    private static EffectiveThemeProvider Create(AppTheme chosen, bool systemIsDark) =>
        new(new FakeSettingsService(SettingsWith(chosen)), new FakeSystemTheme(systemIsDark));

    private static AppSettings SettingsWith(AppTheme theme) =>
        AppSettings.Default with { Appearance = AppearanceSettings.Default with { Theme = theme } };

    /// <summary>Windows-Erscheinungsbild als fester Wert statt aus der Registry.</summary>
    private sealed class FakeSystemTheme(bool isDarkMode) : ISystemThemeProvider
    {
        public bool IsDarkMode { get; } = isDarkMode;
    }
}
