namespace MdExplorer.Core.Models;

/// <summary>
/// Tag-Entität — eindeutig über <see cref="Slug"/>. Der <see cref="Name"/> behält die Original-Schreibweise
/// (inkl. Umlauten gemäß <c>prinzipien.md</c>), der Slug ist die normalisierte URL-/Vergleichs-Variante.
/// </summary>
public sealed class Tag
{
    /// <summary>Primärschlüssel.</summary>
    public Guid Id { get; set; }

    /// <summary>Original-Tag-Name in Ursprungsschreibweise.</summary>
    public required string Name { get; set; }

    /// <summary>Eindeutiger, normalisierter Slug — Lowercase, Bindestriche statt Whitespace, Umlaute bleiben erhalten.</summary>
    public required string Slug { get; set; }
}
