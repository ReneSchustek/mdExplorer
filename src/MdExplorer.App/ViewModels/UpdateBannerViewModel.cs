using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Messaging;
using MdExplorer.App.Services;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Der Hinweis über dem Hauptfenster, dass eine neuere Fassung bereitsteht.
/// </summary>
/// <remarks>
/// Die Meldung kommt vom Hintergrunddienst der Update-Prüfung auf einem Hintergrund-Thread an
/// und wird hinter die Arbeit des UI-Threads gestellt.
/// </remarks>
internal sealed partial class UpdateBannerViewModel : ObservableObject, IDisposable, IRecipient<UpdateAvailableMessage>
{
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IMessenger _messenger;
    private bool _disposed;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string _updateVersion = string.Empty;

    [ObservableProperty]
    private Uri? _updateReleaseUrl;

    /// <summary>Meldet sich für Update-Nachrichten an.</summary>
    public UpdateBannerViewModel(IUiDispatcher uiDispatcher, IMessenger messenger)
    {
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(messenger);

        _uiDispatcher = uiDispatcher;
        _messenger = messenger;
        _messenger.Register(this);
    }

    /// <summary>Blendet den Hinweis für die gemeldete Fassung ein.</summary>
    public void Receive(UpdateAvailableMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _uiDispatcher.Post(() =>
        {
            UpdateVersion = message.Version;
            UpdateReleaseUrl = message.ReleaseUrl;
            IsUpdateAvailable = true;
        });
    }

    /// <summary>Meldet sich ab.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _messenger.Unregister<UpdateAvailableMessage>(this);
    }

    /// <summary>Schließt den Hinweis, ohne die Release-Seite zu öffnen.</summary>
    [RelayCommand]
    private void Dismiss() => IsUpdateAvailable = false;
}
