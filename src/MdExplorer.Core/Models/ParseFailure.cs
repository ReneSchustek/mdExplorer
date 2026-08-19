namespace MdExplorer.Core.Models;

/// <summary>
/// Vermerk über einen gescheiterten Parse-Versuch, 1:1 zu <see cref="MarkdownFile"/> über
/// <see cref="MarkdownFileId"/>. Der Vermerk hält fest, an welchem Inhalt und mit welcher
/// Parser-Fassung der Versuch gescheitert ist; solange beides gleich bleibt, wird die Datei
/// nicht erneut geparst.
/// </summary>
/// <remarks>
/// Eigene Tabelle statt eines Feldes an <see cref="MarkdownDocument"/>: Ein Dokument entsteht
/// erst durch einen erfolgreichen Parse-Vorgang. Ein Fehlschlag müsste sonst ein leeres
/// Dokument anlegen, das Suche, Graph und Anzeige mitschleppen — der Fehlschlag ist ein
/// eigener Sachverhalt und bekommt deshalb eine eigene Tabelle.
/// </remarks>
public sealed class ParseFailure
{
    /// <summary>Primärschlüssel.</summary>
    public Guid Id { get; set; }

    /// <summary>Fremdschlüssel auf <see cref="MarkdownFile.Id"/> (Unique — höchstens ein Vermerk je Datei).</summary>
    public Guid MarkdownFileId { get; set; }

    /// <summary>Navigation auf die zugehörige <see cref="Models.MarkdownFile"/> — für EF-Core-Cascade-Delete und Fluent-Mapping.</summary>
    public MarkdownFile? MarkdownFile { get; set; }

    /// <summary>Hash des Quell-Markdowns, an dem der Versuch gescheitert ist.</summary>
    public required string ContentHash { get; set; }

    /// <summary>Kennung der Parser-Fassung, unter der der Versuch gescheitert ist.</summary>
    public required string EngineVersion { get; set; }

    /// <summary>Kurzbeschreibung des Fehlschlags (Ausnahmetyp und Meldung, gekappt) — für die Diagnose ohne Protokollsuche.</summary>
    public required string FailureReason { get; set; }

    /// <summary>Zeitpunkt des letzten Fehlschlags (UTC).</summary>
    public DateTime FailedAtUtc { get; set; }
}
