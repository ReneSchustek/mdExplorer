using System.Collections.Immutable;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.Extensions.FileSystemGlobbing;

namespace MdExplorer.Core.Settings;

/// <summary>Ergebnis einer Validierung (leere Liste = gültig).</summary>
/// <param name="Errors">Liste der Befunde (pro Property + Index).</param>
public sealed record SettingsValidationResult(ImmutableArray<SettingsValidationError> Errors)
{
    /// <summary><see langword="true"/>, wenn keine Fehler gemeldet wurden.</summary>
    public bool IsValid => Errors.IsEmpty;
}
