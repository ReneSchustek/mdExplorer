using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>
/// Ein Datei-Dienst, der nichts anfasst.
/// </summary>
/// <remarks>
/// Für Tests, die den Zusammenhangs-Bereich brauchen, aber keine Datei-Vorgänge prüfen.
/// Wer die Vorgänge prüfen will, nutzt <see cref="RecordingDocumentFileService"/> — ein
/// Dienst, der still das Richtige tut, würde Fehler in den Aufrufen verdecken.
/// </remarks>
internal sealed class StubDocumentFileService : IDocumentFileService
{
    public Task<DocumentImpact> GetImpactAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
        Task.FromResult(DocumentImpact.Unknown);

    public Task<DocumentFileResult> RenameAsync(Guid markdownFileId, string newFileName, CancellationToken cancellationToken) =>
        Task.FromResult(DocumentFileResult.Failed("Diese Attrappe führt keine Vorgänge aus."));

    public Task<DocumentFileResult> MoveAsync(Guid markdownFileId, string targetDirectory, CancellationToken cancellationToken) =>
        Task.FromResult(DocumentFileResult.Failed("Diese Attrappe führt keine Vorgänge aus."));

    public Task<DocumentFileResult> DeleteAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
        Task.FromResult(DocumentFileResult.Failed("Diese Attrappe führt keine Vorgänge aus."));
}

/// <summary>Merkt sich, was aufgerufen wurde, und antwortet nach Vorgabe.</summary>
internal sealed class RecordingDocumentFileService : IDocumentFileService
{
    /// <summary>Was <see cref="GetImpactAsync"/> meldet.</summary>
    public DocumentImpact Impact { get; set; } = DocumentImpact.Unknown;

    /// <summary>Was die Vorgänge zurückgeben.</summary>
    public DocumentFileResult Result { get; set; } = new(true, "Erledigt.", @"C:\notes\Neu.md");

    /// <summary>Der zuletzt übergebene neue Name.</summary>
    public string? RenamedTo { get; private set; }

    /// <summary>Das zuletzt übergebene Zielverzeichnis.</summary>
    public string? MovedTo { get; private set; }

    /// <summary>Ob gelöscht wurde.</summary>
    public bool DeleteCalled { get; private set; }

    public Task<DocumentImpact> GetImpactAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
        Task.FromResult(Impact);

    public Task<DocumentFileResult> RenameAsync(Guid markdownFileId, string newFileName, CancellationToken cancellationToken)
    {
        RenamedTo = newFileName;
        return Task.FromResult(Result);
    }

    public Task<DocumentFileResult> MoveAsync(Guid markdownFileId, string targetDirectory, CancellationToken cancellationToken)
    {
        MovedTo = targetDirectory;
        return Task.FromResult(Result);
    }

    public Task<DocumentFileResult> DeleteAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        DeleteCalled = true;
        return Task.FromResult(Result);
    }
}
