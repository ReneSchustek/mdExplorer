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
        CurrentVersion = Resolve(assembly);
    }

    /// <inheritdoc />
    public SemanticVersion CurrentVersion { get; }

    private static SemanticVersion Resolve(Assembly assembly)
    {
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (SemanticVersion.TryParse(informational, out SemanticVersion fromInformational))
        {
            return fromInformational;
        }

        Version? assemblyVersion = assembly.GetName().Version;
        if (assemblyVersion is not null
            && SemanticVersion.TryParse(assemblyVersion.ToString(), out SemanticVersion fromAssembly))
        {
            return fromAssembly;
        }

        return new SemanticVersion(0, 0, 0);
    }
}
