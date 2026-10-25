using System.IO;

namespace MdExplorer.App.Tests;

/// <summary>
/// Hält die Dateizuordnung im Setup-Skript fest: MdExplorer steht unter „Öffnen mit“ für
/// <c>.md</c> und <c>.markdown</c>, ohne sich zum Standard zu erklären, und das Deinstallieren
/// entfernt alles wieder.
/// </summary>
/// <remarks>
/// Das Setup wird nur zum Release gebaut, und eine fehlende Zeile fällt erst auf, wenn ein Nutzer
/// den Eintrag vermisst oder nach dem Deinstallieren einen toten behält.
/// </remarks>
public sealed class InstallerGuardTests
{
    private const string ProgId = "MdExplorer.Markdown";
    private const string OpenCommand = "ValueData: \"\"\"{app}\\{#MyAppExeName}\"\" \"\"%1\"\"\"";

    [Fact]
    public void TheProgId_OpensTheFileWithTheProgramAndCarriesItsIcon()
    {
        string[] lines = RegistryLines();

        Assert.Contains(lines, line => line.Contains($@"Software\Classes\{ProgId}""", StringComparison.Ordinal) && line.Contains("Flags: uninsdeletekey", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains($@"Software\Classes\{ProgId}\DefaultIcon""", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains($@"Software\Classes\{ProgId}\shell\open\command""", StringComparison.Ordinal) && line.Contains(OpenCommand, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(".md")]
    [InlineData(".markdown")]
    public void BothExtensions_OfferTheProgramWithoutClaimingTheDefault(string extension)
    {
        string[] lines = RegistryLines();

        // Nur der eigene Wert unter OpenWithProgids, und nur der fällt beim Deinstallieren weg:
        // Den Schlüssel der Endung teilen sich alle Programme, die Markdown öffnen.
        string openWith = Assert.Single(lines, line => line.Contains($@"Software\Classes\{extension}\OpenWithProgids""", StringComparison.Ordinal));
        Assert.Contains($"ValueName: \"{ProgId}\"", openWith, StringComparison.Ordinal);
        Assert.Contains("Flags: uninsdeletevalue", openWith, StringComparison.Ordinal);
        Assert.Contains(lines, line => line.Contains(@"\SupportedTypes""", StringComparison.Ordinal) && line.Contains($"ValueName: \"{extension}\"", StringComparison.Ordinal));
        // Ein Wert ohne Namen direkt unter der Endung setzte das Standardprogramm still um.
        Assert.DoesNotContain(lines, line => line.Contains($@"Software\Classes\{extension}""", StringComparison.Ordinal));
    }

    [Fact]
    public void TheApplicationEntry_IsRemovedOnUninstall()
    {
        string[] lines = RegistryLines();

        Assert.Contains(lines, line => line.Contains(@"Software\Classes\Applications\{#MyAppExeName}""", StringComparison.Ordinal) && line.Contains("Flags: uninsdeletekey", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains(@"Software\Classes\Applications\{#MyAppExeName}\shell\open\command""", StringComparison.Ordinal) && line.Contains(OpenCommand, StringComparison.Ordinal));
    }

    [Fact]
    public void TheSetup_TellsTheExplorerAboutTheNewAssociation()
    {
        Assert.Contains("ChangesAssociations=yes", File.ReadAllLines(ScriptPath()));
    }

    private static string ScriptPath() => Path.Combine(RepositoryPaths.Root(), "installer", "MdExplorer.iss");

    private static string[] RegistryLines() =>
        [.. File.ReadAllLines(ScriptPath()).Where(line => line.StartsWith("Root: HKA;", StringComparison.Ordinal))];
}
