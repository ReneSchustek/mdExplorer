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

    /// <summary>Der Fehler, mit dem die Prüfung scheitert; <see langword="null"/>, wenn sie gelingen soll.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Wird beim ersten Aufruf erfüllt; ein Test im Hintergrund wartet darauf statt auf die Uhr.</summary>
    public Task Called => _called.Task;

    private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken) =>
        CheckForUpdateAsync(force: false, cancellationToken);

    /// <inheritdoc />
    public Task<UpdateCheckResult> CheckForUpdateAsync(bool force, CancellationToken cancellationToken)
    {
        CallCount++;
        LastForce = force;
        _ = _called.TrySetResult();
        return Failure is not null ? Task.FromException<UpdateCheckResult>(Failure) : Task.FromResult(_result);
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
        // Wie die echte Umsetzung: ein bereits gesetzter Abbruch beendet den Vorgang,
        // bevor irgendetwas geschrieben oder gestartet wird.
        cancellationToken.ThrowIfCancellationRequested();
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
