using System.Collections.ObjectModel;
using MdExplorer.TagCloud.Models;

namespace MdExplorer.TagCloud.Messaging;

/// <summary>
/// Nachricht — die Hintergrund-Aktualisierung der Tag-Statistik ist fertig und
/// liefert eine neue Snapshot-Liste. Der Empfänger (i. d. R. das
/// <c>TagCloudViewModel</c>) ersetzt sein <see cref="ObservableCollection{T}"/> auf
/// dem UI-Thread.
/// </summary>
/// <param name="Snapshot">Top-N-Tags absteigend nach Häufigkeit; bei Gleichstand alphabetisch.</param>
public sealed record TagsRefreshedMessage(IReadOnlyList<TagStatistic> Snapshot);
