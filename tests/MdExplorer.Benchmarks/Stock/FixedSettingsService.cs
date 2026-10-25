using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.Benchmarks.Stock;

/// <summary>
/// Einstellungen mit dem Messbestand als einziger Wurzel. Die echte Einstellungsdatei des Nutzers
/// bleibt unberührt; eine Messung darf weder seine Wurzeln indizieren noch etwas speichern.
/// </summary>
internal sealed class FixedSettingsService(string rootPath) : ISettingsService
{
    /// <inheritdoc />
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged
    {
        // Die Einstellungen ändern sich während einer Messung nicht; es gibt nichts zu melden.
        add
        {
            // Absichtlich leer, siehe oben.
        }

        remove
        {
            // Absichtlich leer, siehe oben.
        }
    }

    /// <inheritdoc />
    public AppSettings Current { get; } = AppSettings.Default with
    {
        Indexing = IndexingSettings.Default with { Roots = [rootPath] },
    };

    /// <inheritdoc />
    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

    /// <inheritdoc />
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Der Messbestand hat feste Einstellungen.");
}
