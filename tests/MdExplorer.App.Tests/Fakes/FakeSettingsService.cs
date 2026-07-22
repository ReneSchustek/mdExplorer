using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>Test-Fake für <see cref="ISettingsService"/> — hält den Stand im Speicher, protokolliert Saves.</summary>
internal sealed class FakeSettingsService : ISettingsService
{
    /// <summary>Erzeugt den Fake mit einem Ausgangs-Stand.</summary>
    public FakeSettingsService(AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        Current = current;
    }

    /// <inheritdoc />
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    /// <inheritdoc />
    public AppSettings Current { get; private set; }

    /// <summary>Der zuletzt via <see cref="SaveAsync"/> übergebene Stand, oder <see langword="null"/>.</summary>
    public AppSettings? SavedSettings { get; private set; }

    /// <summary>Wenn gesetzt, wirft <see cref="SaveAsync"/> diese Ausnahme.</summary>
    public Exception? SaveException { get; set; }

    /// <inheritdoc />
    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

    /// <inheritdoc />
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (SaveException is not null)
        {
            return Task.FromException(SaveException);
        }
        AppSettings previous = Current;
        SavedSettings = settings;
        Current = settings;
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(previous, settings));
        return Task.CompletedTask;
    }
}
