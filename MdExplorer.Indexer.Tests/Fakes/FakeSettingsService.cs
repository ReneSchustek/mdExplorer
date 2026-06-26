using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.Indexer.Tests.Fakes;

/// <summary>Test-Doppel für <see cref="ISettingsService"/>. Hält den Stand in-memory und löst Events aus.</summary>
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
