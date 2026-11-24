using System.Diagnostics.CodeAnalysis;
using MdExplorer.App.Services;
using MdExplorer.Core.FileSystem;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;

namespace MdExplorer.App.Hosting;

/// <summary>Was ein Start mitbringt: wer der Benutzer ist und welche Datei geöffnet werden soll.</summary>
/// <param name="Identity">Namen von Sperre und Kanal.</param>
/// <param name="Validator">Prüft Pfade, auch die später weitergereichten.</param>
/// <param name="File">Die geprüfte Datei aus der Befehlszeile.</param>
[ExcludeFromCodeCoverage]
internal sealed record InstanceStartup(InstanceIdentity Identity, MarkdownPathValidator Validator, MarkdownPathCheck File)
{
    /// <summary>Liest den Start aus der Befehlszeile.</summary>
    /// <param name="arguments">Die Befehlszeile.</param>
    public static InstanceStartup FromCommandLine(IReadOnlyList<string> arguments)
    {
        MarkdownPathValidator validator = new(new LocalFileSystem());
        return new InstanceStartup(
            InstanceIdentity.ForCurrentUser(),
            validator,
            validator.CheckCommandLine(arguments, Environment.CurrentDirectory));
    }

    /// <summary>
    /// Derselbe Start ohne abgewiesene Datei: Die Meldung dazu hat <see cref="FirstInstance.HandOverAsync"/>
    /// schon gezeigt, sie soll nicht ein zweites Mal kommen.
    /// </summary>
    public InstanceStartup WithoutRejectedFile() => File.Problem is null ? this : this with { File = MarkdownPathCheck.None };
}
