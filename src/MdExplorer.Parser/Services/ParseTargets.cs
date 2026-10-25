using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.Parser.Services;

/// <summary>Die Dateien eines Stapels, die einen Parse-Versuch brauchen, mit ihren bekannten Vermerken.</summary>
/// <param name="Targets">Veraltet oder neu und nicht durch einen gültigen Vermerk zurückgehalten.</param>
/// <param name="KnownFailures">Die Vermerke der veralteten Dateien.</param>
internal readonly record struct ParseTargets(
    List<MarkdownSourceSnapshot> Targets,
    IReadOnlyDictionary<Guid, ParseFailure> KnownFailures)
{
    /// <summary>Wählt aus dem Stapel die Dateien, deren Dokument fehlt oder veraltet ist.</summary>
    /// <returns><see langword="null"/>, wenn keine Datei des Stapels veraltet ist.</returns>
    public static async Task<ParseTargets?> SelectAsync(
        IMarkdownDocumentRepository docRepo,
        IParseFailureRepository failureRepo,
        List<MarkdownSourceSnapshot> batch,
        string engineVersion,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> stale = await docRepo.GetStaleOrMissingAsync(
            batch.ToDictionary(snapshot => snapshot.Id, snapshot => snapshot.ContentHash), cancellationToken).ConfigureAwait(false);
        if (stale.Count == 0)
        {
            return null;
        }

        HashSet<Guid> staleSet = [.. stale];
        List<MarkdownSourceSnapshot> candidates = [.. batch.Where(snapshot => staleSet.Contains(snapshot.Id))];
        IReadOnlyDictionary<Guid, ParseFailure> knownFailures = await failureRepo
            .GetByMarkdownFileIdsAsync([.. candidates.Select(snapshot => snapshot.Id)], cancellationToken)
            .ConfigureAwait(false);
        return new ParseTargets(
            [.. candidates.Where(snapshot => NeedsParseAttempt(snapshot, knownFailures, engineVersion))],
            knownFailures);
    }

    /// <summary>Ein Vermerk gilt nur für genau den Inhalt und genau die Parser-Fassung, an denen er entstanden ist.</summary>
    private static bool IsStillBinding(ParseFailure failure, MarkdownSourceSnapshot snapshot, string engineVersion) =>
        string.Equals(failure.ContentHash, snapshot.ContentHash, StringComparison.Ordinal)
        && string.Equals(failure.EngineVersion, engineVersion, StringComparison.Ordinal);

    /// <summary>
    /// Ohne diese Prüfung stünde eine unparsbare Datei bei jedem Takt wieder im Stapel: Sie
    /// bekommt kein Dokument, bleibt damit dauerhaft veraltet und würde alle paar Sekunden neu
    /// gelesen, dekodiert und durch den Parser geschickt, für ein Ergebnis, das längst feststeht.
    /// </summary>
    private static bool NeedsParseAttempt(
        MarkdownSourceSnapshot snapshot,
        IReadOnlyDictionary<Guid, ParseFailure> knownFailures,
        string engineVersion) =>
        !knownFailures.TryGetValue(snapshot.Id, out ParseFailure? failure)
        || !IsStillBinding(failure, snapshot, engineVersion);
}
