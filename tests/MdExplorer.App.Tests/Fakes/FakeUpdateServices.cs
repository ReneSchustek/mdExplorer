using MdExplorer.Update.Abstractions;
using MdExplorer.Update.Models;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>Update-Prüfer, der ein vorgegebenes Ergebnis liefert und Aufrufe mitschreibt.</summary>
internal sealed class FakeUpdateChecker : IUpdateChecker
{
    private readonly UpdateCheckResult _result;

    /// <summary>Erzeugt den Fake mit dem zu liefernden Ergebnis.</summary>
    public FakeUpdateChecker(UpdateCheckResult result) => _result = result;

    /// <summary>Anzahl der Prüfaufrufe.</summary>
    public int CallCount { get; private set; }

    /// <summary>Wert des <c>force</c>-Parameters beim letzten Aufruf.</summary>
    public bool LastForce { get; private set; }

    /// <inheritdoc />
    public Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken) =>
        CheckForUpdateAsync(force: false, cancellationToken);

    /// <inheritdoc />
    public Task<UpdateCheckResult> CheckForUpdateAsync(bool force, CancellationToken cancellationToken)
    {
        CallCount++;
        LastForce = force;
        return Task.FromResult(_result);
    }
}

/// <summary>Installer, der ein vorgegebenes Download-Ergebnis liefert und den Start mitschreibt.</summary>
internal sealed class FakeUpdateInstaller : IUpdateInstaller
{
    private readonly UpdateDownloadResult _result;
    private readonly bool _startSucceeds;

    /// <summary>Erzeugt den Fake.</summary>
    public FakeUpdateInstaller(UpdateDownloadResult result, bool startSucceeds = true)
    {
        _result = result;
        _startSucceeds = startSucceeds;
    }

    /// <summary>Pfad, mit dem <see cref="StartInstaller"/> aufgerufen wurde.</summary>
    public string? StartedPath { get; private set; }

    /// <inheritdoc />
    public Task<UpdateDownloadResult> DownloadAndVerifyAsync(
        UpdateAsset asset,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(100);
        return Task.FromResult(_result);
    }

    /// <inheritdoc />
    public bool StartInstaller(string installerPath)
    {
        StartedPath = installerPath;
        return _startSucceeds;
    }
}
