namespace MdExplorer.App.Tests;

/// <summary>
/// Smoke-Tests des App-Projekts: prüft, dass die Assembly geladen werden kann.
/// </summary>
public sealed class SmokeTests
{
    [Fact]
    public void AssemblyLoad_LoadsAppAssemblyWithoutError()
    {
        System.Reflection.Assembly assembly = typeof(MdExplorer.App.Hosting.AppHostBuilder).Assembly;

        Assert.NotNull(assembly);
        Assert.Equal("MdExplorer", assembly.GetName().Name);
    }
}
