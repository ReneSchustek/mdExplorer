using MdExplorer.App.Logging;
using Microsoft.Extensions.Configuration;
using Serilog.Events;

namespace MdExplorer.App.Tests.Logging;

/// <summary>
/// Die Protokollstufe lässt sich zur Diagnose über die Konfiguration anheben. Eine falsche Angabe
/// darf den Start nicht verhindern und lässt die Voreinstellung stehen.
/// </summary>
public sealed class LogLevelSettingTests
{
    [Theory]
    [InlineData("Debug", LogEventLevel.Debug)]
    [InlineData("verbose", LogEventLevel.Verbose)]
    [InlineData("Warning", LogEventLevel.Warning)]
    public void Read_KnownLevel_IsUsed(string configured, LogEventLevel expected)
        => Assert.Equal(expected, LogLevelSetting.Read(ConfigurationWith(configured)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Laut")]
    [InlineData("7")]
    public void Read_MissingOrUnknown_FallsBackToInformation(string? configured)
        => Assert.Equal(LogEventLevel.Information, LogLevelSetting.Read(ConfigurationWith(configured)));

    private static IConfiguration ConfigurationWith(string? level) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [LogLevelSetting.Key] = level })
            .Build();
}
