using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>Merkt sich jeden Verweis, statt ein Programm zu starten.</summary>
internal sealed class RecordingLinkLauncher : IExternalLinkLauncher
{
    /// <summary>Die geöffneten Verweise in Aufrufreihenfolge.</summary>
    public List<Uri> Opened { get; } = [];

    public void Open(Uri uri) => Opened.Add(uri);
}
