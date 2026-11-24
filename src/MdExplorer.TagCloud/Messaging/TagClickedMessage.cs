namespace MdExplorer.TagCloud.Messaging;

/// <summary>
/// Nachricht: der Anwender hat einen Tag in der Cloud angeklickt.
/// Der Empfänger, in der Regel das Such-ViewModel, fügt einen entsprechenden Filter
/// in das aktive Such-Query ein.
/// </summary>
/// <param name="Slug">Normalisierter Tag-Slug (Lowercase, Umlaute erhalten).</param>
/// <param name="DisplayName">Original-Tag-Name für UI-Anzeige/Logs.</param>
/// <param name="Mode">Filter-Modus (additiv, ersetzend oder exkludierend).</param>
public sealed record TagClickedMessage(string Slug, string DisplayName, TagFilterMode Mode);
