using System.Globalization;
using MdExplorer.App.Services.Help;

namespace MdExplorer.App.ViewModels.Help;

/// <summary>
/// ViewModel für den „Über MdExplorer…"-Dialog. Liest die Daten beim Erzeugen
/// einmalig vom <see cref="IAboutInfoProvider"/>; ein Refresh zur Laufzeit ist
/// nicht vorgesehen.
/// </summary>
internal sealed class AboutViewModel
{
    /// <summary>Erzeugt das ViewModel und füllt es mit den aktuellen Werten.</summary>
    public AboutViewModel(IAboutInfoProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        AboutInfo info = provider.Read();
        Version = info.Version;
        BuildDateDisplay = info.BuildDateUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        Libraries = info.Libraries;
    }

    /// <summary>Anzeige-Version (z. B. <c>1.0.0+git-sha</c>).</summary>
    public string Version { get; }

    /// <summary>Build-Datum als formatierter Anzeige-String.</summary>
    public string BuildDateDisplay { get; }

    /// <summary>Liste der eingesetzten Open-Source-Bibliotheken.</summary>
    public IReadOnlyList<LibraryInfo> Libraries { get; }
}
