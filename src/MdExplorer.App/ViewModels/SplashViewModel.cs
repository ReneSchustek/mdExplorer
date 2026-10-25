using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using MdExplorer.App.Localization;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// ViewModel des SplashScreens. Hält Status-Text und Versionsanzeige.
/// </summary>
internal sealed partial class SplashViewModel : ObservableObject
{
    /// <summary>Aktueller Statustext, der unter dem Logo angezeigt wird.</summary>
    [ObservableProperty]
    private string _statusText = UiText.Get("Splash_Status_Initializing");

    /// <summary>Anzeigeversion der Anwendung aus der <c>InformationalVersion</c>, ohne den
    /// Commit-Hash, den der Build per <c>SourceRevisionId</c> hinter einem <c>+</c> anhängt.</summary>
    public string VersionText { get; } = GetDisplayVersion();

    /// <summary>Schneidet den Commit-Hash hinter dem <c>+</c> ab.</summary>
    /// <param name="informationalVersion">Die Informationsversion, etwa <c>0.15.5+3f2a9c1d</c>.</param>
    /// <returns>Die Version ohne Hash.</returns>
    internal static string StripCommitSuffix(string informationalVersion)
    {
        int plusIndex = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return plusIndex < 0 ? informationalVersion : informationalVersion[..plusIndex];
    }

    private static string GetDisplayVersion()
    {
        Assembly entryAssembly = Assembly.GetEntryAssembly() ?? typeof(SplashViewModel).Assembly;
        AssemblyInformationalVersionAttribute? attribute = entryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        string raw = attribute?.InformationalVersion
            ?? entryAssembly.GetName().Version?.ToString()
            ?? "0.0.0";
        return StripCommitSuffix(raw);
    }
}
