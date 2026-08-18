using MdExplorer.Core;

namespace MdExplorer.Core.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void GetApplicationDataDirectory_ReturnsExistingDirectoryUnderLocalAppData()
    {
        string actual = AppPaths.GetApplicationDataDirectory();

        Assert.True(Directory.Exists(actual));
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.StartsWith(localAppData, actual, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(AppPaths.ApplicationFolderName, actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetDatabasePath_EndsWithDatabaseFileName()
    {
        string actual = AppPaths.GetDatabasePath();

        Assert.EndsWith(AppPaths.DatabaseFileName, actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetLogsDirectory_ReturnsExistingDirectoryUnderApplicationData()
    {
        string actual = AppPaths.GetLogsDirectory();

        Assert.True(Directory.Exists(actual));
        Assert.Contains(AppPaths.LogsFolderName, actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetWebView2DataDirectory_LiesUnderTheWritableApplicationDataDirectory()
    {
        // Neben der Programmdatei liegt nach einer Installation C:\Program Files — dort
        // darf die Browser-Komponente nicht schreiben.
        string actual = AppPaths.GetWebView2DataDirectory();

        Assert.StartsWith(AppPaths.GetApplicationDataDirectory(), actual, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(AppPaths.WebView2FolderName, actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetWebView2DataDirectory_IsNotBesideTheProgramFile()
    {
        string actual = AppPaths.GetWebView2DataDirectory();
        string programDirectory = AppContext.BaseDirectory;

        Assert.False(actual.StartsWith(programDirectory, StringComparison.OrdinalIgnoreCase));
    }
}
