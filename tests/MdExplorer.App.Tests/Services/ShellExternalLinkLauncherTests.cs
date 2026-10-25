using System.ComponentModel;
using MdExplorer.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// An die Shell geht nur ein Verweis aus dem Netz oder eine Mail-Adresse. Jedes andere Schema
/// startete das Programm, das Windows dafür eingetragen hat, und das kann alles sein.
/// </summary>
public sealed class ShellExternalLinkLauncherTests
{
    private readonly List<string> _started = [];

    [Theory]
    [InlineData("https://example.org/seite")]
    [InlineData("http://example.org/")]
    [InlineData("mailto:jemand@example.org")]
    public void Open_OnAWebOrMailAddress_HandsItToTheShell(string address)
    {
        CreateSut().Open(new Uri(address));

        Assert.Equal([new Uri(address).AbsoluteUri], _started);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("javascript:alert(1)")]
    [InlineData("search-ms:query=x")]
    public void Open_OnAnyOtherScheme_StartsNothing(string address)
    {
        CreateSut().Open(new Uri(address));

        Assert.Empty(_started);
    }

    [Fact]
    public void Open_OnARelativeAddress_StartsNothing()
    {
        CreateSut().Open(new Uri("bilder/plan.png", UriKind.Relative));

        Assert.Empty(_started);
    }

    /// <remarks>
    /// Ohne Standardprogramm für das Schema meldet Windows einen Fehler. Der Klick bleibt dann
    /// folgenlos, die Anwendung läuft weiter.
    /// </remarks>
    [Fact]
    public void Open_WhenTheShellCannotStartAProgram_DoesNotThrow()
    {
        ShellExternalLinkLauncher sut = new(NullLogger<ShellExternalLinkLauncher>.Instance, _ => throw new Win32Exception(1155));

        Exception? thrown = Record.Exception(() => sut.Open(new Uri("https://example.org/")));

        Assert.Null(thrown);
    }

    private ShellExternalLinkLauncher CreateSut() => new(NullLogger<ShellExternalLinkLauncher>.Instance, _started.Add);
}
