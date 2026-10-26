using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>Sprungliste, die sich jeden gezeigten Stand merkt statt Windows zu fragen.</summary>
internal sealed class RecordingJumpList : IRecentFilesJumpList
{
    public List<IReadOnlyList<string>> Shown { get; } = [];

    public IReadOnlyList<string>? Last => Shown.Count == 0 ? null : Shown[^1];

    public void Show(IReadOnlyList<string> paths) => Shown.Add([.. paths]);
}
