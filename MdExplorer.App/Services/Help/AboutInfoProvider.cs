using System.IO;
using System.Reflection;

namespace MdExplorer.App.Services.Help;

/// <summary>
/// Standard-Implementierung von <see cref="IAboutInfoProvider"/>: liest Version
/// und Build-Datum aus der App-Assembly und liefert eine fest gepflegte Liste
/// der unmittelbar eingesetzten Open-Source-Bibliotheken. Die Liste wird
/// projektweit kuratiert, damit der Dialog sauber bleibt — eine vollständige
/// transitive Inventur folgt aus einem dedizierten Library-Gate-Brief.
/// </summary>
internal sealed class AboutInfoProvider : IAboutInfoProvider
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
        new("Microsoft.Extensions.FileSystemGlobbing", MitLicense),
        new("Microsoft.Extensions.Hosting", MitLicense),
        new("Microsoft.Extensions.Logging", MitLicense),
        new("Microsoft.Extensions.Options.DataAnnotations", MitLicense),
        new("Microsoft.Web.WebView2", MitLicense),
        new("Serilog", Apache20License),
        new("Serilog.Extensions.Hosting", Apache20License),
        new("Serilog.Sinks.Debug", Apache20License),
        new("Serilog.Sinks.File", Apache20License),
        new("SQLitePCLRaw.core", Apache20License),
    ];

    /// <inheritdoc />
    public AboutInfo Read()
    {
        Assembly assembly = typeof(AboutInfoProvider).Assembly;
        string version = ResolveVersion(assembly);
        DateTime buildDate = ResolveBuildDate(assembly);
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

    private static DateTime ResolveBuildDate(Assembly assembly)
    {
        try
        {
            string? location = assembly.Location;
            if (string.IsNullOrEmpty(location) || !File.Exists(location))
            {
                return DateTime.UtcNow;
            }
            return File.GetLastWriteTimeUtc(location);
        }
        catch (IOException)
        {
            return DateTime.UtcNow;
        }
        catch (UnauthorizedAccessException)
        {
            return DateTime.UtcNow;
        }
    }
}
