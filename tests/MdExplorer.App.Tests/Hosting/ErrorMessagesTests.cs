using System.ComponentModel.DataAnnotations;
using System.IO;
using MdExplorer.App.Hosting;
using MdExplorer.App.Localization;
using MdExplorer.App.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace MdExplorer.App.Tests.Hosting;

/// <summary>
/// Die Fehlerdialoge sagen in einem Satz, woran es lag, und kommen aus der Ressourcendatei.
/// </summary>
public sealed class ErrorMessagesTests
{
    public static TheoryData<Exception, string> StartupFailures => new()
    {
        { new OptionsValidationException("Search", typeof(object), ["MaxResults"]), "Startup_Hint_InvalidOptions" },
        { new ValidationException("Ordner fehlt"), "Startup_Hint_InvalidSettings" },
        { new TestDbException("gesperrt"), "Startup_Hint_Database" },
        { new IOException("belegt"), "Startup_Hint_FileAccess" },
        { new UnauthorizedAccessException("verweigert"), "Startup_Hint_AccessDenied" },
        { new InvalidOperationException("Dienst fehlt"), "Startup_Hint_Configuration" },
        { new InvalidCastException("unerwartet"), "Startup_Hint_Unexpected" },
    };

    [Theory]
    [MemberData(nameof(StartupFailures))]
    public void StartupHint_NamesTheCause(Exception exception, string expectedKey)
        => Assert.Equal(UiText.Get(expectedKey), ErrorMessages.StartupHint(exception));

    [Fact]
    public void StartupFailure_ContainsTheHint()
    {
        string message = ErrorMessages.StartupFailure(new IOException("belegt"));

        Assert.Contains(UiText.Get("Startup_Hint_FileAccess"), message, StringComparison.Ordinal);
        Assert.DoesNotContain("[?", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "Error_Unhandled_Hint")]
    [InlineData(true, "Error_Fatal_Hint")]
    public void Unhandled_NamesKindAndMessage(bool isTerminating, string hintKey)
    {
        string message = ErrorMessages.Unhandled(new FormatException("Zahl erwartet"), isTerminating);

        Assert.Contains(UiText.Get(hintKey), message, StringComparison.Ordinal);
        Assert.Contains(nameof(FormatException), message, StringComparison.Ordinal);
        Assert.Contains("Zahl erwartet", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "Error_Unhandled_Title")]
    [InlineData(true, "Error_Fatal_Title")]
    public void UnhandledTitle_DependsOnWhetherTheProcessEnds(bool isTerminating, string titleKey)
        => Assert.Equal(UiText.Get(titleKey), ErrorMessages.UnhandledTitle(isTerminating));
}
