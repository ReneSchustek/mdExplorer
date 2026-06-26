using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// ViewModel des SplashScreens. Hält Status-Text und Versionsanzeige.
/// </summary>
internal sealed partial class SplashViewModel : ObservableObject
{
    /// <summary>Aktueller Statustext, der unter dem Logo angezeigt wird.</summary>
    [ObservableProperty]
    private string _statusText = "Initialisierung läuft …";

    /// <summary>Anzeigeversion der Anwendung, abgeleitet aus dem Assembly-Informational-Version
    /// ohne den per `SourceRevisionId` angehängten Commit-Hash-Suffix.</summary>
    public string VersionText { get; } = GetDisplayVersion();

    private static string GetDisplayVersion()
    {
        Assembly entryAssembly = Assembly.GetEntryAssembly() ?? typeof(SplashViewModel).Assembly;
        AssemblyInformationalVersionAttribute? attribute = entryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        string raw = attribute?.InformationalVersion
            ?? entryAssembly.GetName().Version?.ToString()
            ?? "0.0.0";
        return StripCommitSuffix(raw);
    }

    private static string StripCommitSuffix(string informationalVersion)
    {
        int plusIndex = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return plusIndex < 0 ? informationalVersion : informationalVersion[..plusIndex];
    }
}
