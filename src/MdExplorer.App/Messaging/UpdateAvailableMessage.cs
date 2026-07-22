namespace MdExplorer.App.Messaging;

/// <summary>
/// IMessenger-Notification, die der Update-Hintergrunddienst sendet, sobald eine neuere
/// Version verfügbar ist. Das <see cref="ViewModels.MainViewModel"/> empfängt sie und blendet
/// die Update-Hinweisleiste ein.
/// </summary>
/// <param name="Version">Die neueste verfügbare Version als Anzeigetext.</param>
/// <param name="ReleaseUrl">Browser-URL der Release-Seite.</param>
internal sealed record UpdateAvailableMessage(string Version, Uri ReleaseUrl);
