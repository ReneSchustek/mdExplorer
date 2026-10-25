using System.IO;

namespace MdExplorer.App.Services;

/// <summary>
/// Prüft, ob ein getippter Name ein bloßer Dateiname ist und kein Pfad.
/// </summary>
/// <remarks>
/// Der Dateidienst setzt den neuen Namen an das Verzeichnis der Datei. Steht darin ein Pfad
/// (<c>..\x</c>, <c>C:\anders\x</c>, <c>unter/x</c>), landet die Datei woanders: ein Verschieben
/// unter dem Etikett „Umbenennen“, ohne die Wahl des Ziels, die das Verschieben verlangt.
/// </remarks>
internal static class FileNameRule
{
    /// <summary>Ob <paramref name="name"/> als neuer Dateiname taugt.</summary>
    /// <param name="name">Der getippte Name, mit oder ohne Endung.</param>
    /// <returns>
    /// <see langword="false"/> für einen leeren Namen, für <c>.</c> und <c>..</c> und für jeden
    /// Namen mit einem Zeichen, das Windows in Dateinamen nicht zulässt; dazu gehören beide
    /// Trennzeichen und der Doppelpunkt eines Laufwerks.
    /// </returns>
    public static bool IsPlainName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        string trimmed = name.Trim();
        if (trimmed.Length == 0 || trimmed is "." or "..")
        {
            return false;
        }

        return trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }
}
