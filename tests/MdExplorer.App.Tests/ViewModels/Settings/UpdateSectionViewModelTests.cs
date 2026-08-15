using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels.Settings;
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
        UpdateAsset asset = new("MdExplorer-1.1.0-setup.exe", AssetUrl, new string('a', 64));
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl, asset));
        using UpdateSectionViewModel sut = Build(checker, out _);

        await sut.CheckCommand.ExecuteAsync(null);

        Assert.True(sut.IsInstallAvailable);
        Assert.Contains("1.1.0", sut.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckCommand_WithoutChecksum_DoesNotOfferInstallation()
    {
        // Genau der Fall, den die Prüfsumme absichert: Es gibt eine neuere Fassung, aber
        // keinen Beleg für die Datei. Dann bleibt nur der Weg über die Release-Seite.
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
        UpdateAsset asset = new("MdExplorer-1.1.0-setup.exe", AssetUrl, new string('a', 64));
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
        UpdateAsset asset = new("MdExplorer-1.1.0-setup.exe", AssetUrl, new string('a', 64));
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
    public async Task Dispose_WhileDialogCloses_StartsNoInstaller()
    {
        // Wird der Dialog während des Downloads geschlossen, darf das
        // Installationsprogramm nicht mehr anlaufen: Die Anwendung würde dann nicht
        // beendet und könnte ihre eigenen Dateien nicht ersetzen.
        UpdateAsset asset = new("MdExplorer-1.1.0-setup.exe", AssetUrl, new string('a', 64));
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

    private static UpdateSectionViewModel Build(FakeUpdateChecker checker, out FakeUpdateInstaller installer)
    {
        installer = new FakeUpdateInstaller(UpdateDownloadResult.Verified(@"C:\tmp\setup.exe"));
        return new UpdateSectionViewModel(checker, installer);
    }
}
