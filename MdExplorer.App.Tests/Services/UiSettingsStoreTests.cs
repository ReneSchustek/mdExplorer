using System.IO;
using MdExplorer.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Services;

/// <summary>Tests für die Persistenz der Spaltenbreiten.</summary>
public sealed class UiSettingsStoreTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), "mdexp-ui-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void SaveAndLoad_RoundTripsLayout()
    {
        UiSettingsStore store = new(_tempPath, NullLogger<UiSettingsStore>.Instance);
        UiLayout expected = new(280, 420, 580);

        store.Save(expected);
        UiLayout actual = store.Load();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefault()
    {
        UiSettingsStore store = new(_tempPath, NullLogger<UiSettingsStore>.Instance);

        UiLayout layout = store.Load();

        Assert.Equal(UiLayout.Default, layout);
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath))
        {
            File.Delete(_tempPath);
        }
    }
}
