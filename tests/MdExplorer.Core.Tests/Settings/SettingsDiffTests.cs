using System.Text.Json;
using MdExplorer.Core.Models;
using MdExplorer.Core.Settings;

namespace MdExplorer.Core.Tests.Settings;

/// <summary>
/// Der Verlauf der Einstellungen hält je Änderung Pfad, alten und neuen Wert fest. Meldete der
/// Vergleich unveränderte Werte mit oder verlöre er die Stelle in einer Liste, wäre im Verlauf
/// nicht zu erkennen, was sich geändert hat.
/// </summary>
public sealed class SettingsDiffTests
{
    // Dieselbe Schreibweise wie Datei und Verlauf; eine eigene Kopie liefe auseinander.
    private static readonly JsonSerializerOptions Options = SettingsJson.Options;

    [Fact]
    public void Compute_OnIdenticalSnapshots_ReturnsEmpty()
    {
        AppSettings settings = AppSettings.Default;
        string snapshot = JsonSerializer.Serialize(settings, Options);

        IReadOnlyList<SettingsChangeEntry> result = SettingsDiff.Compute(snapshot, snapshot);

        Assert.Empty(result);
    }

    [Fact]
    public void Compute_OnNestedScalarChange_ReturnsSingleEntry()
    {
        AppSettings previous = AppSettings.Default;
        AppSettings current = previous with
        {
            Behavior = previous.Behavior with { SearchDebounceMs = 750 },
        };
        string previousJson = JsonSerializer.Serialize(previous, Options);
        string currentJson = JsonSerializer.Serialize(current, Options);

        IReadOnlyList<SettingsChangeEntry> result = SettingsDiff.Compute(previousJson, currentJson);

        SettingsChangeEntry entry = Assert.Single(result);
        Assert.Equal("behavior.searchDebounceMs", entry.Path);
        Assert.Equal("300", entry.Previous);
        Assert.Equal("750", entry.Current);
    }

    [Fact]
    public void Compute_OnArrayItemChange_ReportsIndexedPath()
    {
        AppSettings previous = AppSettings.Default with
        {
            Indexing = AppSettings.Default.Indexing with { Roots = ["C:\\A"] },
        };
        AppSettings current = previous with
        {
            Indexing = previous.Indexing with { Roots = ["C:\\A", "C:\\B"] },
        };
        string previousJson = JsonSerializer.Serialize(previous, Options);
        string currentJson = JsonSerializer.Serialize(current, Options);

        IReadOnlyList<SettingsChangeEntry> result = SettingsDiff.Compute(previousJson, currentJson);

        SettingsChangeEntry entry = Assert.Single(result);
        Assert.Equal("indexing.roots[1]", entry.Path);
        Assert.Null(entry.Previous);
        Assert.Equal("\"C:\\\\B\"", entry.Current);
    }

    [Fact]
    public void Compute_OnEnumChange_DetectsTextualDelta()
    {
        AppSettings previous = AppSettings.Default;
        AppSettings current = previous with
        {
            Appearance = previous.Appearance with { Theme = AppTheme.Dark },
        };
        string previousJson = JsonSerializer.Serialize(previous, Options);
        string currentJson = JsonSerializer.Serialize(current, Options);

        IReadOnlyList<SettingsChangeEntry> result = SettingsDiff.Compute(previousJson, currentJson);

        SettingsChangeEntry entry = Assert.Single(result);
        Assert.Equal("appearance.theme", entry.Path);
        Assert.Equal("\"System\"", entry.Previous);
        Assert.Equal("\"Dark\"", entry.Current);
    }
}
