using System.IO;
using System.Text.RegularExpressions;

namespace MdExplorer.App.Tests;

/// <summary>
/// Hält die Absicherung der GitHub-Workflows fest: fremde Actions über ihren Commit, Rechte ab
/// Werk nur lesend, und ein Release erst nach Build, Tests und Abgleich mit <c>VERSION</c>.
/// </summary>
/// <remarks>
/// Die Workflows laufen nicht lokal, und auf GitHub nur, wenn Actions dort laufen. Ohne diesen
/// Wächter fiele ein zurückgefallener Tag oder ein entfernter Prüfschritt niemandem auf, bis
/// ein falsches Release draußen ist.
/// </remarks>
public sealed partial class WorkflowGuardTests
{
    public static TheoryData<string> Workflows =>
        [.. Directory.EnumerateFiles(WorkflowDirectory(), "*.yml").Select(Path.GetFileName).OfType<string>()];

    [Theory]
    [MemberData(nameof(Workflows))]
    public void EveryAction_InAWorkflow_IsPinnedToACommit(string workflow)
    {
        string[] unpinned =
        [
            .. File.ReadAllLines(Path.Combine(WorkflowDirectory(), workflow))
                .Select(line => UsesLine().Match(line))
                .Where(match => match.Success && !PinnedReference().IsMatch(match.Groups["reference"].Value))
                .Select(match => match.Value.Trim()),
        ];

        Assert.True(unpinned.Length == 0, "Nicht über den Commit eingebunden:" + Environment.NewLine + string.Join(Environment.NewLine, unpinned));
    }

    [Theory]
    [MemberData(nameof(Workflows))]
    public void EveryWorkflow_DeclaresReadOnlyPermissionsAtTheTop(string workflow)
    {
        string content = File.ReadAllText(Path.Combine(WorkflowDirectory(), workflow));

        Assert.Matches(TopLevelReadPermission(), content);
    }

    /// <remarks>
    /// Die Reihenfolge zählt: Erst wenn Tag und <c>VERSION</c> übereinstimmen und Build und Tests
    /// bestanden sind, entsteht ein Paket.
    /// </remarks>
    [Fact]
    public void Release_BeforePublishing_ChecksTheVersionBuildsAndTests()
    {
        string release = File.ReadAllText(Path.Combine(WorkflowDirectory(), "release.yml"));
        int publish = release.IndexOf("dotnet publish", StringComparison.Ordinal);

        Assert.True(publish > 0, "Kein Publish-Schritt gefunden.");
        Assert.InRange(release.IndexOf("Get-Content VERSION", StringComparison.Ordinal), 0, publish);
        Assert.InRange(release.IndexOf("dotnet build", StringComparison.Ordinal), 0, publish);
        Assert.InRange(release.IndexOf("dotnet test", StringComparison.Ordinal), 0, publish);
        Assert.Matches(PinnedInnoSetup(), release);
    }

    private static string WorkflowDirectory() => Path.Combine(RepositoryPaths.Root(), ".github", "workflows");

    [GeneratedRegex(@"^\s*(-\s*)?uses:\s*(?<reference>\S+)")]
    private static partial Regex UsesLine();

    // Eigentümer/Name[/Pfad]@ und 40 Zeichen Commit; ein Tag wie @v5 kann umgehängt werden.
    [GeneratedRegex(@"^[\w.-]+/[\w./-]+@[0-9a-f]{40}$")]
    private static partial Regex PinnedReference();

    [GeneratedRegex(@"(?m)^permissions:\s*\r?\n\s+contents:\s*read\s*$")]
    private static partial Regex TopLevelReadPermission();

    [GeneratedRegex(@"choco install innosetup --version \d+\.\d+\.\d+")]
    private static partial Regex PinnedInnoSetup();
}
