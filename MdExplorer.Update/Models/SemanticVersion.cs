using System.Globalization;

namespace MdExplorer.Update.Models;

/// <summary>
/// Minimaler, seiteneffektfreier SemVer-Wert (<c>Major.Minor.Patch</c> mit optionalem
/// Pre-Release-Bezeichner). Bewusst nur so umfangreich wie für den Versionsvergleich des
/// Update-Checks nötig: Build-Metadaten (<c>+sha</c>) werden beim Parsen verworfen, weil sie
/// laut SemVer 2.0 für die Präzedenz irrelevant sind. Ein optionales führendes <c>v</c>
/// (z. B. <c>v0.9.0</c> aus einem Git-Tag) wird toleriert.
/// </summary>
public readonly struct SemanticVersion : IEquatable<SemanticVersion>, IComparable<SemanticVersion>
{
    /// <summary>Erzeugt einen Versionswert aus seinen Bestandteilen.</summary>
    /// <param name="major">Major-Komponente (nicht negativ).</param>
    /// <param name="minor">Minor-Komponente (nicht negativ).</param>
    /// <param name="patch">Patch-Komponente (nicht negativ).</param>
    /// <param name="preRelease">Optionaler Pre-Release-Bezeichner ohne führenden Bindestrich; <see langword="null"/> = stabile Version.</param>
    public SemanticVersion(int major, int minor, int patch, string? preRelease = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);

        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = string.IsNullOrEmpty(preRelease) ? null : preRelease;
    }

    /// <summary>Major-Komponente.</summary>
    public int Major { get; }

    /// <summary>Minor-Komponente.</summary>
    public int Minor { get; }

    /// <summary>Patch-Komponente.</summary>
    public int Patch { get; }

    /// <summary>Pre-Release-Bezeichner ohne Bindestrich, oder <see langword="null"/> für eine stabile Version.</summary>
    public string? PreRelease { get; }

    /// <summary>Größer-Vergleich zweier Versionswerte.</summary>
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    /// <summary>Kleiner-Vergleich zweier Versionswerte.</summary>
    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    /// <summary>Größer-gleich-Vergleich zweier Versionswerte.</summary>
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    /// <summary>Kleiner-gleich-Vergleich zweier Versionswerte.</summary>
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Gleichheits-Operator.</summary>
    public static bool operator ==(SemanticVersion left, SemanticVersion right) => left.Equals(right);

    /// <summary>Ungleichheits-Operator.</summary>
    public static bool operator !=(SemanticVersion left, SemanticVersion right) => !left.Equals(right);

    /// <summary>
    /// Versucht, einen Versionsstring zu parsen. Akzeptiert ein optionales führendes <c>v</c>,
    /// fehlende Patch-/Minor-Komponenten (werden mit 0 aufgefüllt), einen Pre-Release-Suffix
    /// nach <c>-</c> und verwirft Build-Metadaten nach <c>+</c>.
    /// </summary>
    /// <param name="text">Eingabe, z. B. <c>v0.9.0</c>, <c>1.2</c>, <c>1.2.3-beta.1+sha</c>.</param>
    /// <param name="version">Das Ergebnis bei Erfolg, andernfalls <c>default</c>.</param>
    /// <returns><see langword="true"/>, wenn die Eingabe geparst werden konnte.</returns>
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        // Build-Metadaten (SemVer "+...") sind für die Präzedenz irrelevant.
        int plusIndex = trimmed.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex >= 0)
        {
            trimmed = trimmed[..plusIndex];
        }

        string? preRelease = null;
        int dashIndex = trimmed.IndexOf('-', StringComparison.Ordinal);
        if (dashIndex >= 0)
        {
            preRelease = trimmed[(dashIndex + 1)..];
            trimmed = trimmed[..dashIndex];
            if (preRelease.Length == 0)
            {
                return false;
            }
        }

        string[] parts = trimmed.Split('.');
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        int[] numbers = [0, 0, 0];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                return false;
            }
            numbers[i] = value;
        }

        version = new SemanticVersion(numbers[0], numbers[1], numbers[2], preRelease);
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(SemanticVersion other)
    {
        int majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0)
        {
            return majorComparison;
        }
        int minorComparison = Minor.CompareTo(other.Minor);
        if (minorComparison != 0)
        {
            return minorComparison;
        }
        int patchComparison = Patch.CompareTo(other.Patch);
        if (patchComparison != 0)
        {
            return patchComparison;
        }
        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    /// <inheritdoc />
    public bool Equals(SemanticVersion other) =>
        Major == other.Major
        && Minor == other.Minor
        && Patch == other.Patch
        && string.Equals(PreRelease, other.PreRelease, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

    /// <inheritdoc />
    public override string ToString()
    {
        string core = string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
        return PreRelease is null ? core : core + "-" + PreRelease;
    }

    // SemVer 2.0 §11: eine Version OHNE Pre-Release hat höhere Präzedenz als dieselbe MIT
    // Pre-Release. Zwei Pre-Release-Strings werden Identifier-weise verglichen (numerisch vor
    // alphanumerisch, numerische Identifier numerisch).
    private static int ComparePreRelease(string? left, string? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }
        if (left is null)
        {
            return 1;
        }
        if (right is null)
        {
            return -1;
        }

        string[] leftParts = left.Split('.');
        string[] rightParts = right.Split('.');
        int shared = Math.Min(leftParts.Length, rightParts.Length);
        for (int i = 0; i < shared; i++)
        {
            int comparison = CompareIdentifier(leftParts[i], rightParts[i]);
            if (comparison != 0)
            {
                return comparison;
            }
        }
        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private static int CompareIdentifier(string left, string right)
    {
        bool leftNumeric = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out int leftValue);
        bool rightNumeric = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out int rightValue);

        if (leftNumeric && rightNumeric)
        {
            return leftValue.CompareTo(rightValue);
        }
        if (leftNumeric)
        {
            return -1;
        }
        if (rightNumeric)
        {
            return 1;
        }
        return string.CompareOrdinal(left, right);
    }
}
