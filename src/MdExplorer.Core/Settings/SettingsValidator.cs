using System.Collections.Immutable;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.Extensions.FileSystemGlobbing;

namespace MdExplorer.Core.Settings;

/// <summary>
/// Validiert einen <see cref="AppSettings"/>-Stand, bevor er gespeichert wird: Pfade, Glob-Muster,
/// pausierte Ordner, Theme und die Wertebereiche der Zahlen-Einstellungen.
/// </summary>
/// <remarks>
/// <para>
/// Der Befund nennt eine Regel, keinen Satz. Der Core kennt keine Anzeigetexte; welche Worte der
/// Nutzer liest, entscheidet die Oberfläche, und nur dort lassen sie sich übersetzen.
/// </para>
/// <para>
/// Root-Pfade werden nicht gegen Symlink-Ziele kanonisiert. Ein Symlink kann damit auf
/// Systempfade verweisen, ohne dass die Validierung das erkennt. Für eine Einzelplatz-Anwendung,
/// deren Nutzer seine eigenen Ordner einträgt, ist das hinnehmbar; im Mehrbenutzer- oder
/// Server-Betrieb müsste die kanonische Auflösung von Symlinks dazukommen.
/// </para>
/// </remarks>
public sealed class SettingsValidator
{
    // Platzhalter für den Listen-Index bei Feldern, die keine Liste sind.
    private const int ScalarFieldIndex = -1;

    private const string AnyDepthSegment = "**";

    private readonly IFileSystem _fileSystem;

    /// <summary>Erzeugt den Validator und injiziert das Dateisystem.</summary>
    /// <param name="fileSystem">Dateisystem-Abstraktion, über die Root-Pfade geprüft werden.</param>
    public SettingsValidator(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <summary>Validiert die Settings und liefert eine Liste lokaler Fehlerbefunde.</summary>
    /// <param name="settings">Der Stand, der gespeichert werden soll.</param>
    /// <returns>Validierungsergebnis mit gesammelten Befunden; leere Liste = gültig.</returns>
    public SettingsValidationResult Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        List<SettingsValidationError> errors = [];
        ValidateRoots(settings.Indexing.Roots, errors);
        ValidateExclusionPatterns(settings.Indexing.ExclusionPatterns, errors);
        ValidateUiExcludedFolders(settings.Indexing.UiExcludedFolders, errors);
        ValidateTheme(settings.Appearance.Theme, errors);
        ValidateBounds(settings, errors);
        return new SettingsValidationResult(errors.ToImmutableArray());
    }

    private static void ValidateExclusionPatterns(IReadOnlyList<string> patterns, List<SettingsValidationError> errors)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            SettingsRule? rule = CheckPattern(patterns[index]);
            if (rule is not null)
            {
                errors.Add(new SettingsValidationError(SettingsField.ExclusionPatterns, index, rule.Value) { Value = patterns[index] });
            }
        }
    }

    private static SettingsRule? CheckPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return SettingsRule.ValueEmpty;
        }
        string normalized = pattern.StartsWith('!') ? pattern[1..] : pattern;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return SettingsRule.NegationWithoutPattern;
        }

        // Ein absoluter Pfad trifft nie: Die Muster gelten relativ zur Wurzel bzw. zum Ordner der
        // .mdignore. Das Globbing-Paket nimmt ihn trotzdem an, und das Muster wäre still wirkungslos.
        if (Path.IsPathRooted(normalized) || normalized.Contains(':', StringComparison.Ordinal))
        {
            return SettingsRule.PatternRooted;
        }

        // Klammern sind in .gitignore Zeichenklassen bzw. Alternativen. Das Globbing-Paket kennt
        // beides nicht und läse sie als gewöhnliche Zeichen; wer [Dd]rafts schreibt, schlösse
        // dann nichts aus.
        if (normalized.AsSpan().IndexOfAny("[]{}") >= 0)
        {
            return SettingsRule.PatternUnsupportedSyntax;
        }

        return HasMalformedSegment(normalized) || !IsAcceptedByMatcher(normalized)
            ? SettingsRule.PatternInvalid
            : null;
    }

    // "a//b" hat ein leeres Segment, "**a" oder "***" setzen den Tiefen-Platzhalter falsch ein,
    // ".." will aus der Wurzel heraus. Ein abschließender Schrägstrich ("drafts/") bleibt
    // erlaubt: Er bezeichnet in .gitignore-Schreibweise einen Ordner.
    private static bool HasMalformedSegment(string pattern)
    {
        string[] segments = pattern.Replace('\\', '/').TrimEnd('/').Split('/');
        return segments.Any(segment =>
            segment.Length == 0
            || segment == ".."
            || (segment.Contains(AnyDepthSegment, StringComparison.Ordinal) && segment != AnyDepthSegment));
    }

    private static bool IsAcceptedByMatcher(string pattern)
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

    private static void ValidateUiExcludedFolders(IReadOnlyList<string> folders, List<SettingsValidationError> errors)
    {
        for (int index = 0; index < folders.Count; index++)
        {
            string folder = folders[index];
            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
            {
                errors.Add(new SettingsValidationError(SettingsField.UiExcludedFolders, index, SettingsRule.PathNotAbsolute) { Value = folder });
            }
        }
    }

    private static void ValidateTheme(AppTheme theme, List<SettingsValidationError> errors)
    {
        if (!Enum.IsDefined(theme))
        {
            errors.Add(new SettingsValidationError(SettingsField.Theme, ScalarFieldIndex, SettingsRule.UnknownValue) { Value = theme.ToString() });
        }
    }

    private static void ValidateBounds(AppSettings settings, List<SettingsValidationError> errors)
    {
        foreach (BoundedValue bounded in SettingsBounds.Of(settings).Where(bounded => !bounded.IsWithinBounds))
        {
            errors.Add(new SettingsValidationError(bounded.Field, ScalarFieldIndex, SettingsRule.OutOfRange)
            {
                Minimum = bounded.Minimum,
                Maximum = bounded.Maximum,
            });
        }
    }

    private void ValidateRoots(IReadOnlyList<string> roots, List<SettingsValidationError> errors)
    {
        for (int index = 0; index < roots.Count; index++)
        {
            SettingsRule? rule = CheckRoot(roots[index]);
            if (rule is not null)
            {
                errors.Add(new SettingsValidationError(SettingsField.Roots, index, rule.Value) { Value = roots[index] });
            }
        }
    }

    private SettingsRule? CheckRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return SettingsRule.ValueEmpty;
        }
        if (!Path.IsPathFullyQualified(root))
        {
            return SettingsRule.PathNotAbsolute;
        }
        return _fileSystem.DirectoryExists(root) ? null : SettingsRule.DirectoryMissing;
    }
}
