using System.Reflection;
using MdExplorer.Update.Abstractions;
using MdExplorer.Update.Models;

namespace MdExplorer.Update.Services;

/// <summary>
/// Liest die installierte Version aus den Assembly-Attributen. Bevorzugt
/// <see cref="AssemblyInformationalVersionAttribute"/> (entspricht dem <c>VERSION</c>-Inhalt),
/// fällt auf <see cref="AssemblyName.Version"/> zurück und liefert <c>0.0.0</c>, wenn beides fehlt
/// oder unparsbar ist.
/// </summary>
public sealed class AssemblyAppVersionProvider : IAppVersionProvider
{
    /// <summary>Erzeugt den Provider für eine konkrete Assembly (Tests übergeben eine bekannte Assembly).</summary>
    /// <param name="assembly">Die Assembly, deren Version gelesen wird.</param>
    public AssemblyAppVersionProvider(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        CurrentVersion = Resolve(
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            assembly.GetName().Version);
    }

    /// <inheritdoc />
    public SemanticVersion CurrentVersion { get; }

    /// <summary>Wählt aus den beiden Versionsangaben einer Assembly die gültige.</summary>
    /// <param name="informational">Inhalt von <see cref="AssemblyInformationalVersionAttribute"/>.</param>
    /// <param name="assemblyVersion">Die vierstellige Assembly-Version.</param>
    /// <returns>Die Version, oder <c>0.0.0</c>, wenn keine Angabe taugt.</returns>
    internal static SemanticVersion Resolve(string? informational, Version? assemblyVersion)
    {
        if (SemanticVersion.TryParse(informational, out SemanticVersion fromInformational))
        {
            return fromInformational;
        }

        // Die Assembly-Version hat vier Stellen (1.2.3.0); als Text käme sie durch TryParse nicht
        // durch, das nur drei annimmt. Die vierte Stelle hat für den Vergleich mit einer
        // veröffentlichten Fassung keine Bedeutung. Build ist -1, wenn nur zwei Stellen gesetzt sind.
        return assemblyVersion is null
            ? new SemanticVersion(0, 0, 0)
            : new SemanticVersion(assemblyVersion.Major, assemblyVersion.Minor, Math.Max(assemblyVersion.Build, 0));
    }
}
