using System.IO;
using System.Linq;
using MdExplorer.TagCloud.Messaging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Baut Teile einer Suchanfrage, die die Oberfläche selbst einsetzt, statt sie tippen zu lassen.
/// </summary>
internal static class SearchQueryText
{
    /// <summary>
    /// Die Einschränkung auf einen Ordner, relativ zur Wurzel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Der Index speichert relative Pfade mit dem Trennzeichen des Systems; ein Ordner aus der
    /// Anzeige kommt dagegen mit Schrägstrich. Ohne Angleichen träfe <c>notizen/2026</c> keinen
    /// Eintrag unter <c>notizen\2026\</c>, und die Liste bliebe leer.
    /// </para>
    /// <para>
    /// In Anführungszeichen, weil Ordnernamen Leerzeichen enthalten; sonst zerfiele
    /// <c>mein ordner</c> in einen Pfad <c>mein</c> und das Suchwort <c>ordner</c>. Das angehängte
    /// Trennzeichen verhindert, dass <c>docs</c> auch <c>docs-alt</c> trifft.
    /// </para>
    /// </remarks>
    /// <param name="relativeFolder">Der Ordner relativ zur Wurzel, mit beliebigem Trennzeichen.</param>
    /// <returns>Der Anfrageteil <c>path:"…"</c>.</returns>
    public static string PathFilter(string relativeFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFolder);

        string folder = relativeFolder
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar);
        string prefix = folder + Path.DirectorySeparatorChar;

        return "path:\"" + prefix.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>
    /// Setzt eine Kennzeichnung aus der Tag-Cloud in die Anfrage.
    /// </summary>
    /// <remarks>
    /// <see cref="TagFilterMode.Replace"/> ersetzt die Anfrage durch <c>tag:slug</c>,
    /// <see cref="TagFilterMode.Add"/> hängt es an, <see cref="TagFilterMode.Exclude"/> hängt
    /// <c>-tag:slug</c> an. Steht das Stück schon in der Anfrage, bleibt sie, wie sie ist:
    /// Zweimal derselbe Filter ändert die Treffer nicht, macht die Eingabe aber unleserlich.
    /// </remarks>
    /// <param name="existing">Die bisherige Anfrage.</param>
    /// <param name="slug">Die Kennzeichnung.</param>
    /// <param name="mode">Wie die Kennzeichnung wirken soll.</param>
    /// <returns>Die neue Anfrage.</returns>
    public static string WithTag(string existing, string slug, TagFilterMode mode)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        string token = "tag:" + slug;
        return mode switch
        {
            TagFilterMode.Exclude => AppendUnique(existing, "-" + token),
            TagFilterMode.Add => AppendUnique(existing, token),
            _ => token,
        };
    }

    private static string AppendUnique(string existing, string token)
    {
        string trimmed = existing.Trim();
        if (trimmed.Length == 0)
        {
            return token;
        }
        string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Contains(token, StringComparer.Ordinal) ? trimmed : trimmed + " " + token;
    }
}
