using System.IO;
using System.Reflection;

namespace MdExplorer.App.Services.Help;

/// <summary>
/// Standard-Implementierung von <see cref="IAboutInfoProvider"/>: Version aus der App-Assembly,
/// Build-Datum aus der Programmdatei und eine von Hand gepflegte Liste der direkt eingebundenen
/// Open-Source-Bibliotheken. Ein Eintrag steht für ein Paket oder seine Familie
/// (<c>SQLitePCLRaw</c> für Kern und Bündel). Transitive Abhängigkeiten stehen nicht darin; wer ein
/// Paket direkt einbindet, trägt es hier nach, und ein Test gegen die Projektdateien hält das fest.
/// </summary>
internal sealed class AboutInfoProvider(TimeProvider timeProvider) : IAboutInfoProvider
{
    private const string MitLicense = "MIT";
    private const string Apache20License = "Apache-2.0";
    private const string Bsd2ClauseLicense = "BSD-2-Clause";

    private static readonly IReadOnlyList<LibraryInfo> KnownLibraries =
    [
        new("CommunityToolkit.Mvvm", MitLicense),
        new("Markdig", Bsd2ClauseLicense),
        new("Microsoft.Data.Sqlite", MitLicense),
        new("Microsoft.EntityFrameworkCore", MitLicense),
        new("Microsoft.Extensions.DependencyInjection", MitLicense),
        new("Microsoft.Extensions.FileSystemGlobbing", MitLicense),
        new("Microsoft.Extensions.Hosting", MitLicense),
        new("Microsoft.Extensions.Http", MitLicense),
        new("Microsoft.Extensions.Logging", MitLicense),
        new("Microsoft.Extensions.Options", MitLicense),
        new("Microsoft.Web.WebView2", MitLicense),
        new("Serilog", Apache20License),
        new("Serilog.Extensions.Hosting", Apache20License),
        new("Serilog.Sinks.Debug", Apache20License),
        new("Serilog.Sinks.File", Apache20License),
        new("SQLitePCLRaw", Apache20License),
        new("YamlDotNet", MitLicense),
    ];

    /// <inheritdoc />
    public AboutInfo Read()
    {
        Assembly assembly = typeof(AboutInfoProvider).Assembly;
        string version = ResolveVersion(assembly);
        DateTime buildDate = ResolveBuildDate();
        return new AboutInfo(version, buildDate, KnownLibraries);
    }

    private static string ResolveVersion(Assembly assembly)
    {
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }
        Version? version = assembly.GetName().Version;
        return version?.ToString() ?? "0.0.0";
    }

    private DateTime ResolveBuildDate()
    {
        try
        {
            // Environment.ProcessPath ist Single-File-tauglich (anders als Assembly.Location,
            // das im Single-File-Publish leer ist) und zeigt auf die laufende Exe.
            string? processPath = Environment.ProcessPath;
            // Ohne lesbare Programmdatei steht im Dialog die aktuelle Zeit der Anwendungsuhr. Ein
            // ungenaues Datum im Über-Dialog ist kein Grund, ihn nicht zu öffnen.
            if (string.IsNullOrEmpty(processPath) || !File.Exists(processPath))
            {
                return timeProvider.GetUtcNow().UtcDateTime;
            }
            return File.GetLastWriteTimeUtc(processPath);
        }
        catch (IOException)
        {
            return timeProvider.GetUtcNow().UtcDateTime;
        }
        catch (UnauthorizedAccessException)
        {
            return timeProvider.GetUtcNow().UtcDateTime;
        }
    }
}
