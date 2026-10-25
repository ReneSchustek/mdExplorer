using System.ComponentModel;
using MdExplorer.App.ViewModels;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Prüft die Anzeige des Startbilds. Es ist das Erste, was der Nutzer sieht, und die
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
        List<string?> reported = [];
        sut.PropertyChanged += (_, e) => reported.Add(e.PropertyName);

        sut.StatusText = "Bestand wird geladen …";

        Assert.Contains(nameof(SplashViewModel.StatusText), reported, StringComparer.Ordinal);
        Assert.Equal("Bestand wird geladen …", sut.StatusText, StringComparer.Ordinal);
    }

    [Fact]
    public void VersionText_IsPresent()
    {
        SplashViewModel sut = new();

        Assert.False(string.IsNullOrWhiteSpace(sut.VersionText));
    }

    [Theory]
    [InlineData("0.15.5+3f2a9c1d", "0.15.5")]
    [InlineData("0.15.5", "0.15.5")]
    [InlineData("1.0.0-beta.2+abc", "1.0.0-beta.2")]
    public void StripCommitSuffix_DropsTheCommitHash(string informationalVersion, string expected)
    {
        // Beim Bauen hängt SourceRevisionId ein "+<hash>" an die Informationsversion. Auf dem
        // Startbild wäre das nur Rauschen. Geprüft wird die Funktion selbst: Die Version des
        // laufenden Programms ist im Testlauf die des Testprogramms und trägt keinen Hash.
        Assert.Equal(expected, SplashViewModel.StripCommitSuffix(informationalVersion), StringComparer.Ordinal);
    }

    [Fact]
    public void VersionText_IsStableAcrossInstances()
    {
        SplashViewModel first = new();
        SplashViewModel second = new();

        Assert.Equal(first.VersionText, second.VersionText, StringComparer.Ordinal);
    }

    [Fact]
    public void SplashViewModel_ImplementsChangeNotification()
    {
        SplashViewModel sut = new();

        _ = Assert.IsAssignableFrom<INotifyPropertyChanged>(sut);
    }
}
