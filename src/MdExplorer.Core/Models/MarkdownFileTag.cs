namespace MdExplorer.Core.Models;

/// <summary>
/// Join-Entität für die n:m-Beziehung zwischen <see cref="MarkdownFile"/> und <see cref="Tag"/>.
/// </summary>
public sealed class MarkdownFileTag
{
    /// <summary>Fremdschlüssel auf <see cref="MarkdownFile.Id"/> — Teil des zusammengesetzten Primärschlüssels.</summary>
    public Guid MarkdownFileId { get; set; }

    /// <summary>Fremdschlüssel auf <see cref="Tag.Id"/> — Teil des zusammengesetzten Primärschlüssels.</summary>
    public Guid TagId { get; set; }

    /// <summary>Navigation auf die referenzierte <see cref="Models.MarkdownFile"/> — für Cascade-Delete bei File-Löschung.</summary>
    public MarkdownFile? MarkdownFile { get; set; }

    /// <summary>Navigation auf den referenzierten <see cref="Models.Tag"/> — für Cascade-Delete bei Tag-Löschung.</summary>
    public Tag? Tag { get; set; }
}
