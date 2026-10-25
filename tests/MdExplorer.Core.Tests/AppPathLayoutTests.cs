using MdExplorer.Core.Tests.Fakes;

namespace MdExplorer.Core.Tests;

/// <summary>
/// Datenbank, Protokolle und Browserdaten liegen unter <c>%LOCALAPPDATA%</c>. Lägen sie neben
/// der Programmdatei, scheiterte jedes Schreiben, sobald die App unter Programme installiert ist.
/// Die Pfade rechnet die Anwendung aus; angelegt wird erst beim Start und nicht beim Fragen.
/// </summary>
public sealed class AppPathLayoutTests
{
    private const string Root = @"C:\Benutzer\Test\AppData\Local\MdExplorer";

    [Fact]
    public void Default_LiesUnderLocalAppData()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string actual = AppPaths.Default.ApplicationDataDirectory;

        Assert.StartsWith(localAppData, actual, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(AppPaths.ApplicationFolderName, actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Default_DoesNotLieBesideTheProgramFile()
    {
        // Neben der Programmdatei liegt nach einer Installation C:\Program Files; dort darf
        // weder die Datenbank noch die Browser-Komponente schreiben.
        Assert.False(AppPaths.Default.WebView2DataDirectory.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase));
        Assert.False(AppPaths.Default.DatabasePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Paths_AreComposedUnderTheRoot()
    {
        AppPathLayout sut = new(Root);

        Assert.Equal(Path.Combine(Root, AppPaths.DatabaseFileName), sut.DatabasePath);
        Assert.Equal(Path.Combine(Root, AppPaths.LogsFolderName), sut.LogsDirectory);
        Assert.Equal(Path.Combine(Root, AppPaths.SettingsFileName), sut.SettingsPath);
        Assert.Equal(Path.Combine(Root, AppPaths.SettingsHistoryFolderName), sut.SettingsHistoryDirectory);
        Assert.Equal(Path.Combine(Root, AppPaths.SettingsAuditLogFileName), sut.SettingsAuditLogPath);
        Assert.Equal(Path.Combine(Root, AppPaths.WebView2FolderName), sut.WebView2DataDirectory);
    }

    [Fact]
    public void Paths_DoNotCreateAnything()
    {
        // Früher legte jeder Getter nebenbei Ordner an, auch in jedem Test, der nur einen Pfad
        // wissen wollte, und das im Profil des Entwicklers.
        InMemoryFileSystem fileSystem = new();
        AppPathLayout sut = new(Root);

        _ = sut.DatabasePath;
        _ = sut.LogsDirectory;

        Assert.False(fileSystem.DirectoryExists(Root));
    }

    [Fact]
    public void EnsureDirectories_CreatesEveryFolderTheApplicationWritesInto()
    {
        // SQLite legt die Datenbankdatei an, nicht ihren Ordner; die Migration ist der erste
        // Schreibzugriff und braucht ihn.
        InMemoryFileSystem fileSystem = new();
        AppPathLayout sut = new(Root);

        sut.EnsureDirectories(fileSystem);

        Assert.True(fileSystem.DirectoryExists(Root));
        Assert.True(fileSystem.DirectoryExists(sut.LogsDirectory));
        Assert.True(fileSystem.DirectoryExists(sut.SettingsHistoryDirectory));
        Assert.True(fileSystem.DirectoryExists(sut.WebView2DataDirectory));
    }
}
