using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>Deterministisches Erscheinungsbild für Tests.</summary>
internal sealed class FakeThemeProvider(bool isDarkMode) : IEffectiveThemeProvider
{
    public bool IsDarkMode { get; } = isDarkMode;
}
