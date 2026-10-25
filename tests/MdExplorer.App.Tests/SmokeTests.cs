namespace MdExplorer.App.Tests;

/// <summary>
/// Fällt als Erstes um, wenn die App-Assembly sich nicht laden lässt oder nicht mehr
/// „MdExplorer" heißt.
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
