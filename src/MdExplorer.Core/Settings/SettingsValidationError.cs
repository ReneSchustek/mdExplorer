using System.Collections.Immutable;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.Extensions.FileSystemGlobbing;

namespace MdExplorer.Core.Settings;

/// <summary>Einzelner Validierungs-Befund.</summary>
/// <param name="Field">Welches Feld der Settings ist betroffen.</param>
/// <param name="Index">Listen-Index (oder -1 für Skalar-Felder).</param>
/// <param name="Rule">Welche Regel verletzt ist.</param>
public sealed record SettingsValidationError(SettingsField Field, int Index, SettingsRule Rule)
{
    /// <summary>Der beanstandete Wert, sofern er für die Meldung gebraucht wird.</summary>
    public string? Value { get; init; }

    /// <summary>Untergrenze bei <see cref="SettingsRule.OutOfRange"/>.</summary>
    public int Minimum { get; init; }

    /// <summary>Obergrenze bei <see cref="SettingsRule.OutOfRange"/>.</summary>
    public int Maximum { get; init; }
}
