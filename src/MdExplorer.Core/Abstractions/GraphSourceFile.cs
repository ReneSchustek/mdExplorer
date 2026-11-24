namespace MdExplorer.Core.Abstractions;

/// <summary>Minimal-Repräsentation einer indizierten Markdown-Datei für den Graph.</summary>
/// <param name="Id">Stabiler Schlüssel.</param>
/// <param name="FileNameWithoutExtension">Dateiname ohne Erweiterung als Basis für die Slug-Auflösung.</param>
/// <param name="RelativePath">Pfad relativ zum Root.</param>
public sealed record GraphSourceFile(
    Guid Id,
    string FileNameWithoutExtension,
    string RelativePath);
