using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>Hält das Layout im Speicher und zählt, wie oft geschrieben wird.</summary>
internal sealed class FakeUiLayoutStore : IUiLayoutStore
{
    public UiLayout Layout { get; set; } = UiLayout.Default;

    public int SaveCount { get; private set; }

    public UiLayout Load() => Layout;

    public void Save(UiLayout layout)
    {
        Layout = layout;
        SaveCount++;
    }
}
