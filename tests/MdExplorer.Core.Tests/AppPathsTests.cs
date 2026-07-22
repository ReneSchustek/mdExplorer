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
}
