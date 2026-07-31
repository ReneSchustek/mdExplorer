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
    public async Task CheckCommand_UsesForce_SoDerNutzerNichtDieGedrosselteAuskunftBekommt()
    {
        FakeUpdateChecker checker = new(UpdateCheckResult.UpToDate(Current, Current));
        UpdateSectionViewModel sut = Build(checker, out _);

        await sut.CheckCommand.ExecuteAsync(null);

        Assert.True(checker.LastForce);
    }

    [Fact]
    public async Task CheckCommand_MitInstallierbaremUpdate_SchaltetInstallationFrei()
    {
        UpdateAsset asset = new("MdExplorer-1.1.0-setup.exe", AssetUrl, new string('a', 64));
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl, asset));
        UpdateSectionViewModel sut = Build(checker, out _);

        await sut.CheckCommand.ExecuteAsync(null);

        Assert.True(sut.IsInstallAvailable);
        Assert.Contains("1.1.0", sut.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckCommand_OhnePruefsumme_BietetKeineInstallationAn()
    {
        // Genau der Fall, den die Pruefsumme absichert: Es gibt eine neuere Fassung, aber
        // keinen Beleg fuer die Datei. Dann bleibt nur der Weg ueber die Release-Seite.
        UpdateAsset asset = new("MdExplorer-1.1.0-setup.exe", AssetUrl, null);
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl, asset));
        UpdateSectionViewModel sut = Build(checker, out _);

        await sut.CheckCommand.ExecuteAsync(null);

        Assert.False(sut.IsInstallAvailable);
        Assert.Contains("Prüfsumme", sut.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckCommand_OhneUpdate_MeldetAktuellUndBietetNichtsAn()
    {
        FakeUpdateChecker checker = new(UpdateCheckResult.UpToDate(Current, Current));
        UpdateSectionViewModel sut = Build(checker, out _);

        await sut.CheckCommand.ExecuteAsync(null);

        Assert.False(sut.IsInstallAvailable);
        Assert.Contains("aktuell", sut.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallCommand_NachErfolgreicherPruefung_StartetDenInstaller()
    {
        UpdateAsset asset = new("MdExplorer-1.1.0-setup.exe", AssetUrl, new string('a', 64));
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl, asset));
        FakeUpdateInstaller installer = new(UpdateDownloadResult.Verified(@"C:\tmp\setup.exe"));
        UpdateSectionViewModel sut = new(checker, installer);
        bool started = false;
        sut.InstallerStarted += (_, _) => started = true;

        await sut.CheckCommand.ExecuteAsync(null);
        await sut.InstallCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\tmp\setup.exe", installer.StartedPath);
        Assert.True(started);
    }

    [Fact]
    public async Task InstallCommand_BeiAbweichenderPruefsumme_StartetNichts()
    {
        UpdateAsset asset = new("MdExplorer-1.1.0-setup.exe", AssetUrl, new string('a', 64));
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl, asset));
        FakeUpdateInstaller installer = new(UpdateDownloadResult.Failed(UpdateDownloadStatus.ChecksumMismatch));
        UpdateSectionViewModel sut = new(checker, installer);
        bool started = false;
        sut.InstallerStarted += (_, _) => started = true;

        await sut.CheckCommand.ExecuteAsync(null);
        await sut.InstallCommand.ExecuteAsync(null);

        Assert.Null(installer.StartedPath);
        Assert.False(started);
        Assert.Contains("verworfen", sut.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallCommand_OhneVorherigePruefung_TutNichts()
    {
        FakeUpdateChecker checker = new(UpdateCheckResult.UpToDate(Current, Current));
        FakeUpdateInstaller installer = new(UpdateDownloadResult.Verified(@"C:\tmp\setup.exe"));
        UpdateSectionViewModel sut = new(checker, installer);

        await sut.InstallCommand.ExecuteAsync(null);

        Assert.Null(installer.StartedPath);
    }

    private static UpdateSectionViewModel Build(FakeUpdateChecker checker, out FakeUpdateInstaller installer)
    {
        installer = new FakeUpdateInstaller(UpdateDownloadResult.Verified(@"C:\tmp\setup.exe"));
        return new UpdateSectionViewModel(checker, installer);
    }
}
