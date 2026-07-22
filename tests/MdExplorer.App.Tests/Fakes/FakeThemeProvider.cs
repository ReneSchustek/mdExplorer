using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>Deterministischer Theme-Provider für Tests.</summary>
internal sealed class FakeThemeProvider(bool isDarkMode) : ISystemThemeProvider
{
    public bool IsDarkMode { get; } = isDarkMode;
}
