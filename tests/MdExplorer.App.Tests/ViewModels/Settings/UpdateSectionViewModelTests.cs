using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels.Settings;
using MdExplorer.Update.Abstractions;
using MdExplorer.Update.Models;

namespace MdExplorer.App.Tests.ViewModels.Settings;

/// <summary>
/// Tests des Update-Abschnitts. Der Schwerpunkt liegt auf dem, was schiefgehen darf und was
/// nicht: Ein Paket ohne belegte Prüfsumme darf nie zur Installation angeboten werden, und
/// eine abweichende Prüfsumme darf nie zum Start des Installationsprogramms führen.
/// </summary>
public sealed class UpdateSectionViewModelTests
{
    private static readonly SemanticVersion Current = new(1, 0, 0);
    private static readonly SemanticVersion Newer = new(1, 1, 0);
    private static readonly Uri ReleaseUrl = new("https://example.invalid/releases/latest");
    private static readonly Uri AssetUrl = new("https://example.invalid/setup.exe");

    [Fact]
    public async Task CheckCommand_UsesForce_SoUserDoesNotGetThrottledResult()
    {
        FakeUpdateChecker checker = new(UpdateCheckResult.UpToDate(Current, Current));
        using UpdateSectionViewModel sut = Build(checker, out _);

        await sut.CheckCommand.ExecuteAsync(null);

        Assert.True(checker.LastForce);
    }

    [Fact]
    public async Task CheckCommand_WithInstallableUpdate_EnablesInstallation()
    {
        // 64 Zeichen haben die Form einer SHA-256-Prüfsumme in Hex; der Wert selbst wird hier
        // nicht nachgerechnet, das Herunterladen ist nachgebildet.
        UpdateAsset asset = new("MdExplorer-1.1.0-setup.exe", AssetUrl, new string('a', 64), new byte[] { 0x30 });
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl, asset));
        using UpdateSectionViewModel sut = Build(checker, out _);

        await sut.CheckCommand.ExecuteAsync(null);

        Assert.True(sut.IsInstallAvailable);
        Assert.Contains("1.1.0", sut.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckCommand_WithoutChecksum_DoesNotOfferInstallation()
    {
        // Es gibt eine neuere Fassung, aber keinen Beleg für die Datei. Dann bleibt nur der
        // Weg über die Release-Seite.
        UpdateAsset asset = new("MdExplorer-1.1.0-setup.exe", AssetUrl, null);
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl, asset));
        using UpdateSectionViewModel sut = Build(checker, out _);

        await sut.CheckCommand.ExecuteAsync(null);

        Assert.False(sut.IsInstallAvailable);
        Assert.Contains("Prüfsumme", sut.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckCommand_WithoutUpdate_ReportsCurrentAndOffersNothing()
    {
        FakeUpdateChecker checker = new(UpdateCheckResult.UpToDate(Current, Current));
        using UpdateSectionViewModel sut = Build(checker, out _);

        await sut.CheckCommand.ExecuteAsync(null);

        Assert.False(sut.IsInstallAvailable);
        Assert.Contains("aktuell", sut.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallCommand_AfterSuccessfulVerification_StartsInstaller()
    {
        UpdateAsset asset = new("MdExplorer-1.1.0-setup.exe", AssetUrl, new string('a', 64), new byte[] { 0x30 });
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl, asset));
        FakeUpdateInstaller installer = new(UpdateDownloadResult.Verified(@"C:\tmp\setup.exe"));
        using UpdateSectionViewModel sut = new(checker, installer);
        bool started = false;
        sut.InstallerStarted += (_, _) => started = true;

        await sut.CheckCommand.ExecuteAsync(null);
        await sut.InstallCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\tmp\setup.exe", installer.StartedPath);
        Assert.True(started);
    }

    [Fact]
    public async Task InstallCommand_OnChecksumMismatch_StartsNothing()
    {
        UpdateAsset asset = new("MdExplorer-1.1.0-setup.exe", AssetUrl, new string('a', 64), new byte[] { 0x30 });
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl, asset));
        FakeUpdateInstaller installer = new(UpdateDownloadResult.Failed(UpdateDownloadStatus.ChecksumMismatch));
        using UpdateSectionViewModel sut = new(checker, installer);
        bool started = false;
        sut.InstallerStarted += (_, _) => started = true;

        await sut.CheckCommand.ExecuteAsync(null);
        await sut.InstallCommand.ExecuteAsync(null);

        Assert.Null(installer.StartedPath);
        Assert.False(started);
        Assert.Contains("verworfen", sut.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallCommand_WithoutPriorVerification_DoesNothing()
    {
        FakeUpdateChecker checker = new(UpdateCheckResult.UpToDate(Current, Current));
        FakeUpdateInstaller installer = new(UpdateDownloadResult.Verified(@"C:\tmp\setup.exe"));
        using UpdateSectionViewModel sut = new(checker, installer);

        await sut.InstallCommand.ExecuteAsync(null);

        Assert.Null(installer.StartedPath);
    }

    [Fact]
    public async Task Dispose_WhileTheDownloadRuns_StartsNoInstaller()
    {
        // Wird der Dialog während des Downloads geschlossen, darf das Installationsprogramm
        // nicht mehr anlaufen: Die Anwendung würde dann nicht beendet und könnte ihre eigenen
        // Dateien nicht ersetzen.
        UpdateAsset asset = new("MdExplorer-1.1.0-setup.exe", AssetUrl, new string('a', 64), new byte[] { 0x30 });
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl, asset));
        WaitingUpdateInstaller installer = new();
        UpdateSectionViewModel sut = new(checker, installer);
        bool started = false;
        sut.InstallerStarted += (_, _) => started = true;
        await sut.CheckCommand.ExecuteAsync(null);

        Task install = sut.InstallCommand.ExecuteAsync(null);
        await installer.DownloadStarted.Task.ConfigureAwait(true);
        sut.Dispose();
        await install.ConfigureAwait(true);

        Assert.False(installer.StartCalled);
        Assert.False(started);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task Dispose_WhileTheCheckRuns_EndsTheCheckQuietly()
    {
        // Der Dialog lässt sich während der Prüfung schließen. Der Abbruch lief früher aus dem
        // Befehl heraus und landete als unbehandelte Ausnahme beim Aufrufer.
        WaitingUpdateChecker checker = new();
        UpdateSectionViewModel sut = new(checker, new FakeUpdateInstaller(UpdateDownloadResult.Verified(@"C:\tmp\setup.exe")));

        Task check = sut.CheckCommand.ExecuteAsync(null);
        await checker.CheckStarted.Task.ConfigureAwait(true);
        sut.Dispose();
        await check.ConfigureAwait(true);

        Assert.False(sut.IsBusy);
        Assert.False(sut.IsInstallAvailable);
    }

    [Fact]
    public async Task Dispose_BeforeInstalling_StartsNoInstaller()
    {
        UpdateAsset asset = new("MdExplorer-1.1.0-setup.exe", AssetUrl, new string('a', 64), new byte[] { 0x30 });
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl, asset));
        FakeUpdateInstaller installer = new(UpdateDownloadResult.Verified(@"C:\tmp\setup.exe"));
        UpdateSectionViewModel sut = new(checker, installer);
        bool started = false;
        sut.InstallerStarted += (_, _) => started = true;

        await sut.CheckCommand.ExecuteAsync(null);
        sut.Dispose();
        await sut.InstallCommand.ExecuteAsync(null);

        Assert.Null(installer.StartedPath);
        Assert.False(started);
    }

    [Fact]
    public async Task Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Das Fenster meldet sich beim Schließen ab; ein zweiter Aufruf darf nicht
        // über die bereits entsorgte Abbruchquelle stolpern.
        FakeUpdateChecker checker = new(UpdateCheckResult.UpToDate(Current, Current));
        UpdateSectionViewModel sut = Build(checker, out _);

        sut.Dispose();
        sut.Dispose();
        await sut.CheckCommand.ExecuteAsync(null);

        Assert.Equal(0, checker.CallCount);
    }

    /// <remarks>
    /// War die Quelle nicht erreichbar, weiß die Anwendung nicht, ob eine neuere Fassung
    /// existiert, und muss das sagen. Ein „Sie sind aktuell" wäre an dieser Stelle eine
    /// Behauptung, die niemand geprüft hat.
    /// </remarks>
    [Fact]
    public async Task CheckCommand_WhenTheSourceIsUnreachable_SaysItDoesNotKnow()
    {
        FakeUpdateChecker checker = new(UpdateCheckResult.Failed(Current));
        using UpdateSectionViewModel sut = Build(checker, out _);

        await sut.CheckCommand.ExecuteAsync(null);

        Assert.False(sut.IsInstallAvailable);
        Assert.Contains("nicht feststellen", sut.StatusText, StringComparison.Ordinal);
        Assert.Contains("Das heißt nicht, dass Sie aktuell sind", sut.StatusText, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Jede Fehlermeldung des Herunterladens sagt, was passiert ist und was mit der Datei
    /// geschehen ist. „Fehlgeschlagen" allein lässt offen, ob etwas Halbes auf der Platte liegt.
    /// </remarks>
    [Theory]
    [InlineData(UpdateDownloadStatus.DownloadFailed, "fehlgeschlagen")]
    [InlineData(UpdateDownloadStatus.NoChecksumPublished, "keine Prüfsumme")]
    [InlineData(UpdateDownloadStatus.StorageFailed, "nicht ablegen")]
    [InlineData(UpdateDownloadStatus.NoSignaturePublished, "keine Signatur")]
    [InlineData(UpdateDownloadStatus.SignatureInvalid, "nicht vom Herausgeber")]
    public async Task InstallCommand_OnAFailedDownload_SaysWhatHappened(UpdateDownloadStatus status, string expected)
    {
        UpdateAsset asset = new("MdExplorer-1.1.0-setup.exe", AssetUrl, new string('a', 64), new byte[] { 0x30 });
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl, asset));
        FakeUpdateInstaller installer = new(UpdateDownloadResult.Failed(status));
        using UpdateSectionViewModel sut = new(checker, installer);
        bool started = false;
        sut.InstallerStarted += (_, _) => started = true;

        await sut.CheckCommand.ExecuteAsync(null);
        await sut.InstallCommand.ExecuteAsync(null);

        Assert.False(started);
        Assert.Null(installer.StartedPath);
        Assert.Contains(expected, sut.StatusText, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Die Prüfsumme stimmte, aber das Installationsprogramm ließ sich nicht starten. Dann muss
    /// der Pfad zur geprüften Datei in der Meldung stehen: Sie ist heruntergeladen und in
    /// Ordnung, nur der letzte Schritt fehlt, und den kann der Nutzer selbst gehen.
    /// </remarks>
    [Fact]
    public async Task InstallCommand_WhenTheInstallerWillNotStart_NamesTheVerifiedFile()
    {
        const string FilePath = @"C:\tmp\setup.exe";
        UpdateAsset asset = new("MdExplorer-1.1.0-setup.exe", AssetUrl, new string('a', 64), new byte[] { 0x30 });
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl, asset));
        FakeUpdateInstaller installer = new(UpdateDownloadResult.Verified(FilePath), startSucceeds: false);
        using UpdateSectionViewModel sut = new(checker, installer);
        bool started = false;
        sut.InstallerStarted += (_, _) => started = true;

        await sut.CheckCommand.ExecuteAsync(null);
        await sut.InstallCommand.ExecuteAsync(null);

        Assert.False(started);
        Assert.Contains(FilePath, sut.StatusText, StringComparison.Ordinal);
    }

    /// <summary>Endet erst mit dem Abbruch, ohne Zeitgeber und ohne Wanduhr.</summary>
    private static async Task UntilCancelledAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = cancellationToken.Register(() => cancelled.TrySetCanceled(cancellationToken));
        await cancelled.Task.ConfigureAwait(false);
    }

    private static UpdateSectionViewModel Build(FakeUpdateChecker checker, out FakeUpdateInstaller installer)
    {
        installer = new FakeUpdateInstaller(UpdateDownloadResult.Verified(@"C:\tmp\setup.exe"));
        return new UpdateSectionViewModel(checker, installer);
    }

    /// <summary>Eine Prüfung, die erst mit dem Abbruch endet.</summary>
    private sealed class WaitingUpdateChecker : IUpdateChecker
    {
        public TaskCompletionSource CheckStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken) =>
            CheckForUpdateAsync(force: false, cancellationToken);

        public async Task<UpdateCheckResult> CheckForUpdateAsync(bool force, CancellationToken cancellationToken)
        {
            CheckStarted.SetResult();
            await UntilCancelledAsync(cancellationToken).ConfigureAwait(false);
            return UpdateCheckResult.UpToDate(Current, Current);
        }
    }

    /// <summary>Ein Download, der erst mit dem Abbruch endet, so wie die echte Umsetzung.</summary>
    private sealed class WaitingUpdateInstaller : IUpdateInstaller
    {
        public TaskCompletionSource DownloadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool StartCalled { get; private set; }

        public async Task<UpdateDownloadResult> DownloadAndVerifyAsync(UpdateAsset asset, IProgress<int>? progress, CancellationToken cancellationToken)
        {
            DownloadStarted.SetResult();
            await UntilCancelledAsync(cancellationToken).ConfigureAwait(false);
            return UpdateDownloadResult.Verified(@"C:\tmp\setup.exe");
        }

        public bool StartInstaller(string installerPath)
        {
            StartCalled = true;
            return true;
        }
    }
}
