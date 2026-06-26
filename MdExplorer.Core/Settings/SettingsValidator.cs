using System.Collections.Immutable;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.Extensions.FileSystemGlobbing;

namespace MdExplorer.Core.Settings;

/// <summary>
/// Validiert einen <see cref="AppSettings"/>-Stand bevor er gespeichert wird.
/// Prüft Pfad-Existenz, syntaktische Validität der Glob-Muster und
/// Wertebereich der Verhaltens-Parameter.
/// </summary>
/// <remarks>
/// Sicherheits-Hinweis: Root-Pfade werden nicht gegen Symlink-Ziele kanonisiert.
/// Ein Symlink kann damit auf Systempfade verweisen, ohne dass die Validierung das erkennt.
/// MdExplorer geht davon aus, dass der Endanwender Vertrauen in seine eigene Workspace-Konfiguration hat —
/// im Multi-User- oder Server-Kontext muss die kanonische Aufloesung von Symlinks ergaenzt werden.
/// </remarks>
public sealed class SettingsValidator
{
    private const int MinPreviewFontSize = 8;
    private const int MaxPreviewFontSize = 64;
    private const int MinResultsPerPage = 10;
    private const int MaxResultsPerPage = 1_000;
    private const int MinSearchDebounceMs = 50;
    private const int MaxSearchDebounceMs = 5_000;
    private const int MinIndexerResyncSeconds = 0;
    private const int MaxIndexerResyncSeconds = 3_600;
    private const int ScalarFieldIndex = -1;

    private readonly IFileSystem _fileSystem;

    /// <summary>Erzeugt den Validator und injiziert das Dateisystem.</summary>
    /// <param name="fileSystem">Dateisystem-Abstraktion, ueber die Root-Pfade gepruefte werden.</param>
    public SettingsValidator(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <summary>Validiert die Settings und liefert eine Liste lokaler Fehlerbefunde.</summary>
    /// <param name="settings">Settings-Snapshot, der gegen die Senior-Pflichten geprueft wird.</param>
    /// <returns>Validierungsergebnis mit gesammelten Befunden; leere Liste = gueltig.</returns>
    public SettingsValidationResult Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        List<SettingsValidationError> errors = [];
        ValidateRoots(settings.Indexing.Roots, errors);
        ValidateExclusionPatterns(settings.Indexing.ExclusionPatterns, errors);
        ValidateAppearance(settings.Appearance, errors);
        ValidateBehavior(settings.Behavior, errors);
        return new SettingsValidationResult(errors.ToImmutableArray());
    }

    private static void ValidateExclusionPatterns(IReadOnlyList<string> patterns, List<SettingsValidationError> errors)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            string pattern = patterns[index];
            if (string.IsNullOrWhiteSpace(pattern))
            {
                errors.Add(new SettingsValidationError(SettingsField.ExclusionPatterns, index, "Muster ist leer."));
                continue;
            }
            string normalized = pattern.StartsWith('!') ? pattern[1..] : pattern;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                errors.Add(new SettingsValidationError(SettingsField.ExclusionPatterns, index, "Negation ohne Muster."));
                continue;
            }
            if (!IsSyntacticallyValidGlob(normalized))
            {
                errors.Add(new SettingsValidationError(SettingsField.ExclusionPatterns, index, $"Ungültiges Glob-Muster: '{pattern}'."));
            }
        }
    }

    private static void ValidateAppearance(AppearanceSettings appearance, List<SettingsValidationError> errors)
    {
        if (appearance.PreviewFontSize is < MinPreviewFontSize or > MaxPreviewFontSize)
        {
            errors.Add(new SettingsValidationError(SettingsField.PreviewFontSize, ScalarFieldIndex, $"Schriftgröße muss zwischen {MinPreviewFontSize} und {MaxPreviewFontSize} Pixel liegen."));
        }
        if (appearance.ResultsPerPage is < MinResultsPerPage or > MaxResultsPerPage)
        {
            errors.Add(new SettingsValidationError(SettingsField.ResultsPerPage, ScalarFieldIndex, $"Trefferanzahl muss zwischen {MinResultsPerPage} und {MaxResultsPerPage} liegen."));
        }
    }

    private static void ValidateBehavior(BehaviorSettings behavior, List<SettingsValidationError> errors)
    {
        if (behavior.SearchDebounceMs is < MinSearchDebounceMs or > MaxSearchDebounceMs)
        {
            errors.Add(new SettingsValidationError(SettingsField.SearchDebounceMs, ScalarFieldIndex, $"Such-Debounce muss zwischen {MinSearchDebounceMs} und {MaxSearchDebounceMs} ms liegen."));
        }
        if (behavior.IndexerResyncIntervalSeconds is < MinIndexerResyncSeconds or > MaxIndexerResyncSeconds)
        {
            errors.Add(new SettingsValidationError(SettingsField.IndexerResyncIntervalSeconds, ScalarFieldIndex, $"Resync-Intervall muss zwischen {MinIndexerResyncSeconds} und {MaxIndexerResyncSeconds} Sekunden liegen."));
        }
    }

    private void ValidateRoots(IReadOnlyList<string> roots, List<SettingsValidationError> errors)
    {
        for (int index = 0; index < roots.Count; index++)
        {
            string root = roots[index];
            if (string.IsNullOrWhiteSpace(root))
            {
                errors.Add(new SettingsValidationError(SettingsField.Roots, index, "Pfad ist leer."));
                continue;
            }
            if (!Path.IsPathFullyQualified(root))
            {
                errors.Add(new SettingsValidationError(SettingsField.Roots, index, $"Pfad muss absolut sein: '{root}'."));
                continue;
            }
            if (!_fileSystem.DirectoryExists(root))
            {
                errors.Add(new SettingsValidationError(SettingsField.Roots, index, $"Verzeichnis existiert nicht: '{root}'."));
            }
        }
    }

    /// <summary>
    /// Probiert, das Pattern an einem Matcher anzulegen, und beobachtet die Match-Operation
    /// auf einer Dummy-Eingabe. Das Globbing-Paket wirft bei syntaktischen Fehlern; abgefangene
    /// Ausnahmen werden als invalides Muster gewertet.
    /// </summary>
    private static bool IsSyntacticallyValidGlob(string pattern)
    {
        try
        {
            Matcher matcher = new(StringComparison.OrdinalIgnoreCase);
            _ = matcher.AddInclude(pattern);
            _ = matcher.Match("dummy.md");
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>Ergebnis einer Validierung — leere Liste = gültig.</summary>
/// <param name="Errors">Liste der Befunde (pro Property + Index).</param>
public sealed record SettingsValidationResult(ImmutableArray<SettingsValidationError> Errors)
{
    /// <summary><see langword="true"/>, wenn keine Fehler gemeldet wurden.</summary>
    public bool IsValid => Errors.IsEmpty;
}

/// <summary>Einzelner Validierungs-Befund.</summary>
/// <param name="Field">Welches Feld der Settings ist betroffen.</param>
/// <param name="Index">Listen-Index (oder -1 für Skalar-Felder).</param>
/// <param name="Message">Menschen-lesbare Fehlermeldung.</param>
public sealed record SettingsValidationError(SettingsField Field, int Index, string Message);

/// <summary>Klassifizierung des betroffenen Settings-Feldes.</summary>
public enum SettingsField
{
    /// <summary>Roots-Liste (Indexer).</summary>
    Roots,

    /// <summary>Ausschluss-Muster-Liste (Indexer).</summary>
    ExclusionPatterns,

    /// <summary>Preview-Schriftgröße.</summary>
    PreviewFontSize,

    /// <summary>Trefferanzahl pro Seite.</summary>
    ResultsPerPage,

    /// <summary>Such-Debounce in Millisekunden.</summary>
    SearchDebounceMs,

    /// <summary>Indexer-Resync-Intervall in Sekunden.</summary>
    IndexerResyncIntervalSeconds,
}
