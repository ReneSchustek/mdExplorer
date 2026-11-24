using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using Microsoft.Win32;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Die Belegung wird nur dann neu angewendet, wenn sich das geltende Erscheinungsbild wirklich
/// geändert hat, und nur Änderungen in Windows, die es betreffen können, lösen die Prüfung aus.
/// </summary>
public sealed class AppliedThemeTests
{
    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(false, false, true, false, true)]
    [InlineData(false, false, false, true, true)]
    [InlineData(true, true, true, false, true)]
    public void DiffersFrom_OnlyWhenDarkModeOrContrastChanged(bool appliedDark, bool appliedContrast, bool nowDark, bool nowContrast, bool expected)
    {
        AppliedTheme sut = new(appliedDark, appliedContrast);

        Assert.Equal(expected, sut.DiffersFrom(new FakeThemeProvider(nowDark, nowContrast)));
    }

    [Fact]
    public void From_TakesBothValuesOfTheProvider()
    {
        Assert.Equal(new AppliedTheme(true, true), AppliedTheme.From(new FakeThemeProvider(isDarkMode: true, isHighContrast: true)));
    }

    [Theory]
    [InlineData(UserPreferenceCategory.General, true)]
    [InlineData(UserPreferenceCategory.Color, false)]
    [InlineData(UserPreferenceCategory.Mouse, false)]
    public void MayChangeTheTheme_OnlyForGeneralChanges(UserPreferenceCategory category, bool expected)
    {
        Assert.Equal(expected, AppliedTheme.MayChangeTheTheme(category));
    }
}
