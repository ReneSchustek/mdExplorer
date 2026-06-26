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

    /// <summary>Navigation auf die referenzierte <see cref="Models.MarkdownFile"/> — fuer Cascade-Delete bei File-Loeschung.</summary>
    public MarkdownFile? MarkdownFile { get; set; }

    /// <summary>Navigation auf den referenzierten <see cref="Models.Tag"/> — fuer Cascade-Delete bei Tag-Loeschung.</summary>
    public Tag? Tag { get; set; }
}
