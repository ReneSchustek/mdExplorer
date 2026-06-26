using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.Parser.Tests.Helpers;

/// <summary>
/// In-Memory-<see cref="ISettingsService"/> fuer Parser-Unit-Tests. Haelt den Stand,
/// loest <see cref="SettingsChanged"/> beim <see cref="SaveAsync"/>-Aufruf aus.
/// </summary>
internal sealed class FakeSettingsService : ISettingsService
{
    public FakeSettingsService(AppSettings? initial = null)
    {
        Current = initial ?? AppSettings.Default;
    }

    public AppSettings Current { get; private set; }

    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AppSettings previous = Current;
        Current = settings;
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(previous, settings));
        return Task.CompletedTask;
    }
}
