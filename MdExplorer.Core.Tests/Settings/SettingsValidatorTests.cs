using System.IO;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Core.Settings;

namespace MdExplorer.Core.Tests.Settings;

public sealed class SettingsValidatorTests
{
    [Fact]
    public void Validate_OnValidSettings_ReportsNoErrors()
    {
        StubFileSystem fs = new();
        _ = fs.Directories.Add(@"C:\Notes");
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
        StubFileSystem fs = new();
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
        StubFileSystem fs = new();
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
        StubFileSystem fs = new();
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
        StubFileSystem fs = new();
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
        StubFileSystem fs = new();
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
        StubFileSystem fs = new();
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
        StubFileSystem fs = new();
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

    private sealed class StubFileSystem : IFileSystem
    {
        public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool DirectoryExists(string path) => Directories.Contains(path);
        public bool FileExists(string path) => false;
        public void EnsureDirectoryExists(string path) => Directories.Add(path);
        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) => [];
        public IEnumerable<string> EnumerateDirectories(string directory) => [];
        public bool IsReparsePoint(string path) => false;
        public string GetDirectoryFinalPath(string path) => path;
        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) => Task.FromResult(Array.Empty<byte>());
        public byte[] ReadAllBytes(string path) => [];
        public Stream OpenRead(string path) => Stream.Null;
        public DateTime GetLastWriteTimeUtc(string path) => DateTime.UnixEpoch;
        public long GetFileSize(string path) => 0;
        public Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
