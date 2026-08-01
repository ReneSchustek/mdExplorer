using System.ComponentModel;
using MdExplorer.App.ViewModels;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Prüft die Anzeige des Startbilds. Es ist das Erste, was der Nutzer sieht — und die
/// Versionsanzeige darf den beim Bauen angehängten Commit-Hash nicht mitzeigen.
/// </summary>
public sealed class SplashViewModelTests
{
    [Fact]
    public void StatusText_ByDefault_IsNotEmpty()
    {
        SplashViewModel sut = new();

        Assert.False(string.IsNullOrWhiteSpace(sut.StatusText));
    }

    [Fact]
    public void StatusText_WhenChanged_RaisesPropertyChanged()
    {
        // Ohne die Benachrichtigung bliebe der Text während des Starts auf dem ersten Wert stehen.
        SplashViewModel sut = new();
        List<string?> gemeldet = [];
        sut.PropertyChanged += (_, e) => gemeldet.Add(e.PropertyName);

        sut.StatusText = "Bestand wird geladen …";

        Assert.Contains(nameof(SplashViewModel.StatusText), gemeldet, StringComparer.Ordinal);
        Assert.Equal("Bestand wird geladen …", sut.StatusText, StringComparer.Ordinal);
    }

    [Fact]
    public void VersionText_IsPresent()
    {
        SplashViewModel sut = new();

        Assert.False(string.IsNullOrWhiteSpace(sut.VersionText));
    }

    [Fact]
    public void VersionText_DropsTheCommitHashSuffix()
    {
        // Beim Bauen hängt SourceRevisionId ein "+<hash>" an die Informationsversion.
        // Auf dem Startbild wäre das nur Rauschen.
        SplashViewModel sut = new();

        Assert.DoesNotContain("+", sut.VersionText, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionText_IsStableAcrossInstances()
    {
        SplashViewModel erste = new();
        SplashViewModel zweite = new();

        Assert.Equal(erste.VersionText, zweite.VersionText, StringComparer.Ordinal);
    }

    [Fact]
    public void SplashViewModel_ImplementsChangeNotification()
    {
        SplashViewModel sut = new();

        _ = Assert.IsAssignableFrom<INotifyPropertyChanged>(sut);
    }
}
