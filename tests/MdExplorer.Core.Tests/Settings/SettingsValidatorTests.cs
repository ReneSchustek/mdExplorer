using MdExplorer.Core.Models;
using MdExplorer.Core.Settings;
using MdExplorer.Core.Tests.Fakes;

namespace MdExplorer.Core.Tests.Settings;

/// <summary>
/// Ungültige Einstellungen werden mit Feld und Position gemeldet, bevor sie gespeichert werden:
/// fehlende oder relative Wurzeln, leere Muster und Zahlen außerhalb ihrer Grenzen. Die
/// Theorien prüfen jeweils eine Einheit neben den Grenzen des Validators, bei der
/// Entprellzeit auch die Grenzwerte selbst.
/// </summary>
public sealed class SettingsValidatorTests
{
    [Fact]
    public void Validate_OnValidSettings_ReportsNoErrors()
    {
        InMemoryFileSystem fs = new();
        fs.AddDirectory(@"C:\Notes");
        SettingsValidator sut = new(fs);

        AppSettings input = new(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings([@"C:\Notes"], ["**/Drafts/**"], [], true),
            AppearanceSettings.Default,
            BehaviorSettings.Default);

        SettingsValidationResult result = sut.Validate(input);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_OnNonExistentRoot_ReportsError()
    {
        InMemoryFileSystem fs = new();
        SettingsValidator sut = new(fs);

        AppSettings input = new(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings([@"C:\does-not-exist"], [], [], true),
            AppearanceSettings.Default,
            BehaviorSettings.Default);

        SettingsValidationResult result = sut.Validate(input);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == SettingsField.Roots);
    }

    [Fact]
    public void Validate_OnRelativePath_ReportsError()
    {
        InMemoryFileSystem fs = new();
        SettingsValidator sut = new(fs);

        AppSettings input = new(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings(["./relative"], [], [], true),
            AppearanceSettings.Default,
            BehaviorSettings.Default);

        SettingsValidationResult result = sut.Validate(input);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == SettingsField.Roots);
    }

    [Fact]
    public void Validate_OnEmptyExclusion_ReportsError()
    {
        InMemoryFileSystem fs = new();
        SettingsValidator sut = new(fs);

        AppSettings input = new(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings([], ["", "  "], [], true),
            AppearanceSettings.Default,
            BehaviorSettings.Default);

        SettingsValidationResult result = sut.Validate(input);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count(e => e.Field == SettingsField.ExclusionPatterns));
    }

    [Theory]
    [InlineData(7, SettingsField.PreviewFontSize)]
    [InlineData(65, SettingsField.PreviewFontSize)]
    public void Validate_OnPreviewFontSizeOutOfRange_ReportsError(int fontSize, SettingsField expectedField)
    {
        InMemoryFileSystem fs = new();
        SettingsValidator sut = new(fs);

        AppSettings input = new(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings([], [], [], true),
            new AppearanceSettings(AppTheme.System, fontSize, 50),
            BehaviorSettings.Default);

        SettingsValidationResult result = sut.Validate(input);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == expectedField);
    }

    [Theory]
    [InlineData(9, SettingsField.ResultsPerPage)]
    [InlineData(1001, SettingsField.ResultsPerPage)]
    public void Validate_OnResultsPerPageOutOfRange_ReportsError(int perPage, SettingsField expectedField)
    {
        InMemoryFileSystem fs = new();
        SettingsValidator sut = new(fs);

        AppSettings input = new(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings([], [], [], true),
            new AppearanceSettings(AppTheme.System, 16, perPage),
            BehaviorSettings.Default);

        SettingsValidationResult result = sut.Validate(input);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == expectedField);
    }

    [Theory]
    [InlineData(49, true)]
    [InlineData(50, false)]
    [InlineData(5_000, false)]
    [InlineData(5_001, true)]
    public void Validate_OnSearchDebounceBoundary_DetectsRangeViolations(int debounceMs, bool expectError)
    {
        InMemoryFileSystem fs = new();
        SettingsValidator sut = new(fs);

        AppSettings input = new(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings([], [], [], true),
            AppearanceSettings.Default,
            new BehaviorSettings(debounceMs, 300));

        SettingsValidationResult result = sut.Validate(input);

        Assert.Equal(expectError, !result.IsValid);
    }

    [Fact]
    public void Validate_OnAppearanceOutOfRange_ReportsError()
    {
        InMemoryFileSystem fs = new();
        SettingsValidator sut = new(fs);

        AppSettings input = new(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings([], [], [], true),
            new AppearanceSettings(AppTheme.System, 5, 9999),
            BehaviorSettings.Default);

        SettingsValidationResult result = sut.Validate(input);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == SettingsField.PreviewFontSize);
        Assert.Contains(result.Errors, e => e.Field == SettingsField.ResultsPerPage);
    }

    [Fact]
    public void Validate_OnBlankRoot_ReportsError()
    {
        // Ein leerer Eintrag entsteht, wenn im Einstellungsdialog eine Zeile stehen bleibt.
        // Ohne Befund liefe der Indexer gegen einen leeren Pfad.
        InMemoryFileSystem fs = new();
        SettingsValidator sut = new(fs);

        SettingsValidationResult result = sut.Validate(new AppSettings(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings(["   "], [], [], true),
            AppearanceSettings.Default,
            BehaviorSettings.Default));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == SettingsField.Roots && e.Index == 0);
    }

    [Fact]
    public void Validate_OnNegationWithoutPattern_ReportsError()
    {
        // "!" allein ist eine Ausnahme ohne Gegenstand; sie würde stillschweigend nichts tun.
        InMemoryFileSystem fs = new();
        SettingsValidator sut = new(fs);

        SettingsValidationResult result = sut.Validate(new AppSettings(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings([], ["!"], [], true),
            AppearanceSettings.Default,
            BehaviorSettings.Default));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == SettingsField.ExclusionPatterns && e.Index == 0);
    }

    [Fact]
    public void Validate_OnResyncIntervalOutOfRange_ReportsError()
    {
        InMemoryFileSystem fs = new();
        SettingsValidator sut = new(fs);

        SettingsValidationResult result = sut.Validate(new AppSettings(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings([], [], [], true),
            AppearanceSettings.Default,
            // Weit über der Obergrenze von einer Stunde.
            BehaviorSettings.Default with { IndexerResyncIntervalSeconds = 99_999 }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == SettingsField.IndexerResyncIntervalSeconds);
    }

    [Fact]
    public void Validate_OnANegatedPattern_AcceptsIt()
    {
        // Die Negation ist gültige Syntax und darf keinen Befund erzeugen.
        InMemoryFileSystem fs = new();
        SettingsValidator sut = new(fs);

        SettingsValidationResult result = sut.Validate(new AppSettings(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings([], ["!wichtig/**"], [], true),
            AppearanceSettings.Default,
            BehaviorSettings.Default));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithoutSettings_Throws()
    {
        InMemoryFileSystem fs = new();
        SettingsValidator sut = new(fs);

        _ = Assert.Throws<ArgumentNullException>(() => sut.Validate(null!));
    }

    [Fact]
    public void Constructor_WithoutFileSystem_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new SettingsValidator(null!));
    }

    [Theory]
    [InlineData("", SettingsRule.ValueEmpty)]
    [InlineData("!", SettingsRule.NegationWithoutPattern)]
    [InlineData(@"C:\Notizen\**", SettingsRule.PatternRooted)]
    [InlineData("/absolut/**", SettingsRule.PatternRooted)]
    [InlineData("[Dd]rafts/**", SettingsRule.PatternUnsupportedSyntax)]
    [InlineData("{a,b}/**", SettingsRule.PatternUnsupportedSyntax)]
    [InlineData("a//b", SettingsRule.PatternInvalid)]
    [InlineData("**a/b", SettingsRule.PatternInvalid)]
    [InlineData("***", SettingsRule.PatternInvalid)]
    [InlineData("a/../b", SettingsRule.PatternInvalid)]
    public void Validate_OnAPatternThatCannotWork_NamesTheRule(string pattern, SettingsRule expected)
    {
        // Vorher nahm die Prüfung fast alles an, weil das Globbing-Paket kaum wirft: Ein absoluter
        // Pfad oder eine Zeichenklasse lief durch und schloss danach still nichts aus.
        SettingsValidator sut = new(new InMemoryFileSystem());

        SettingsValidationResult result = sut.Validate(WithPatterns(pattern));

        SettingsValidationError error = Assert.Single(result.Errors);
        Assert.Equal(SettingsField.ExclusionPatterns, error.Field);
        Assert.Equal(expected, error.Rule);
    }

    [Theory]
    [InlineData("**/.git/**")]
    [InlineData("drafts/")]
    [InlineData("**/*.tmp.md")]
    [InlineData("!wichtig/**")]
    public void Validate_OnUsualPatterns_AcceptsThem(string pattern)
    {
        SettingsValidator sut = new(new InMemoryFileSystem());

        Assert.True(sut.Validate(WithPatterns(pattern)).IsValid);
    }

    [Fact]
    public void Validate_OnARelativePausedFolder_ReportsIt()
    {
        // Pausierte Ordner vergleicht der Indexer als absolute Pfade; ein relativer träfe nie.
        SettingsValidator sut = new(new InMemoryFileSystem());
        AppSettings input = AppSettings.Default with { Indexing = AppSettings.Default.Indexing with { UiExcludedFolders = ["notizen"] } };

        SettingsValidationError error = Assert.Single(sut.Validate(input).Errors);

        Assert.Equal(SettingsField.UiExcludedFolders, error.Field);
        Assert.Equal(SettingsRule.PathNotAbsolute, error.Rule);
    }

    [Fact]
    public void Validate_OnAnUnknownTheme_ReportsIt()
    {
        SettingsValidator sut = new(new InMemoryFileSystem());
        AppSettings input = AppSettings.Default with { Appearance = AppSettings.Default.Appearance with { Theme = (AppTheme)42 } };

        SettingsValidationError error = Assert.Single(sut.Validate(input).Errors);

        Assert.Equal(SettingsField.Theme, error.Field);
        Assert.Equal(SettingsRule.UnknownValue, error.Rule);
    }

    [Fact]
    public void Validate_OnAValueOutOfRange_CarriesTheBoundsForTheMessage()
    {
        // Die Oberfläche nennt die Grenzen; sie stehen im Befund, nicht noch einmal in der App.
        SettingsValidator sut = new(new InMemoryFileSystem());
        AppSettings input = AppSettings.Default with { Appearance = AppSettings.Default.Appearance with { PreviewFontSize = 2 } };

        SettingsValidationError error = Assert.Single(sut.Validate(input).Errors);

        Assert.Equal(SettingsRule.OutOfRange, error.Rule);
        Assert.Equal(AppearanceSettings.MinPreviewFontSize, error.Minimum);
        Assert.Equal(AppearanceSettings.MaxPreviewFontSize, error.Maximum);
    }

    [Fact]
    public void Validate_OnAMissingRoot_NamesTheRuleAndThePath()
    {
        SettingsValidator sut = new(new InMemoryFileSystem());
        AppSettings input = AppSettings.Default with { Indexing = AppSettings.Default.Indexing with { Roots = [@"C:\fehlt"] } };

        SettingsValidationError error = Assert.Single(sut.Validate(input).Errors);

        Assert.Equal(SettingsRule.DirectoryMissing, error.Rule);
        Assert.Equal(@"C:\fehlt", error.Value);
    }

    private static AppSettings WithPatterns(params string[] patterns) =>
        AppSettings.Default with { Indexing = AppSettings.Default.Indexing with { ExclusionPatterns = patterns } };
}
