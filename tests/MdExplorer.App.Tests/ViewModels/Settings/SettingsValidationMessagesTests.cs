using MdExplorer.App.ViewModels.Settings;
using MdExplorer.Core.Settings;

namespace MdExplorer.App.Tests.ViewModels.Settings;

/// <summary>
/// Jeder Befund des Validators wird zu einem lesbaren Satz. Fehlte ein Fall, zeigte der Dialog
/// den Platzhalter eines fehlenden Schlüssels oder einen rohen Enum-Namen.
/// </summary>
public sealed class SettingsValidationMessagesTests
{
    public static TheoryData<SettingsRule> Rules => [.. Enum.GetValues<SettingsRule>()];

    public static TheoryData<SettingsField> Fields => [.. Enum.GetValues<SettingsField>()];

    [Theory]
    [MemberData(nameof(Rules))]
    public void Format_ForEveryRule_YieldsARealSentence(SettingsRule rule)
    {
        SettingsValidationError error = new(SettingsField.Roots, 0, rule) { Value = @"C:\Notizen", Minimum = 1, Maximum = 9 };

        string line = SettingsValidationMessages.Format(error);

        Assert.DoesNotContain("[?", line, StringComparison.Ordinal);
        Assert.DoesNotContain(rule.ToString(), line, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public void Format_ForEveryField_NamesItInWords(SettingsField field)
    {
        SettingsValidationError error = new(field, 2, SettingsRule.ValueEmpty);

        string line = SettingsValidationMessages.Format(error);

        Assert.DoesNotContain("[?", line, StringComparison.Ordinal);
        Assert.DoesNotContain(field.ToString(), line, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_CountsListEntriesFromOne()
    {
        // Die Liste im Dialog beginnt für den Nutzer bei 1; „Index-Root 0“ hielte er für falsch.
        string line = SettingsValidationMessages.Format(new SettingsValidationError(SettingsField.Roots, 0, SettingsRule.DirectoryMissing) { Value = @"C:\fehlt" });

        Assert.Contains("1", line, StringComparison.Ordinal);
        Assert.Contains(@"C:\fehlt", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ForAValueOutOfRange_NamesTheBounds()
    {
        string line = SettingsValidationMessages.Format(new SettingsValidationError(SettingsField.PreviewFontSize, -1, SettingsRule.OutOfRange) { Minimum = 8, Maximum = 64 });

        Assert.Contains("8", line, StringComparison.Ordinal);
        Assert.Contains("64", line, StringComparison.Ordinal);
    }
}
