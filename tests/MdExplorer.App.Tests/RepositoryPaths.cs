using System.IO;

namespace MdExplorer.App.Tests;

/// <summary>
/// Pfade im Repository für die Wächtertests, die Quelltext statt Verhalten prüfen.
/// </summary>
internal static class RepositoryPaths
{
    /// <summary>Wurzel des Repositorys, erkannt an der Projektmappe.</summary>
    public static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MdExplorer.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return directory.FullName;
    }

    /// <summary>Der Ordner <c>src</c>.</summary>
    public static string Source() => Path.Combine(Root(), "src");

    /// <summary>Ordner eines Projekts unter <c>src</c>.</summary>
    /// <param name="project">Name des Projekts, etwa <c>MdExplorer.App</c>.</param>
    public static string Project(string project) => Path.Combine(Source(), project);

    /// <summary>Dateien eines Projekts nach Muster, ohne Build-Ausgaben.</summary>
    /// <param name="project">Name des Projekts.</param>
    /// <param name="pattern">Dateimuster, etwa <c>*.xaml</c>.</param>
    public static IEnumerable<string> SourceFiles(string project, string pattern) =>
        Directory.EnumerateFiles(Project(project), pattern, SearchOption.AllDirectories)
            .Where(file =>
            {
                string[] parts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return !parts.Contains("obj") && !parts.Contains("bin");
            });
}
