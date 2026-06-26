using MdExplorer.Core.Models;

namespace MdExplorer.Core.Messaging;

/// <summary>
/// IMessenger-Notification, die nach dem Persistieren einer neuen
/// <see cref="AppSettings"/>-Datei in der UI-Schicht veröffentlicht wird.
/// Module wie der Indexer oder die TagCloud reagieren darauf mit Re-Scan
/// bzw. Re-Render.
/// </summary>
/// <param name="Previous">Letzter Stand vor dem Save.</param>
/// <param name="Current">Neuer Stand.</param>
public sealed record SettingsChangedMessage(AppSettings Previous, AppSettings Current);
