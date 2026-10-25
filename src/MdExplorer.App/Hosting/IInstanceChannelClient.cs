namespace MdExplorer.App.Hosting;

/// <summary>Reicht einen Auftrag an die laufende Instanz weiter.</summary>
internal interface IInstanceChannelClient
{
    /// <summary>Übergibt den Auftrag und wartet auf die Bestätigung.</summary>
    /// <param name="absolutePath">Die Datei; <see langword="null"/>, wenn nur das Fenster nach vorn soll.</param>
    /// <param name="cancellationToken">Abbruch, auch durch die Zeitgrenze.</param>
    /// <exception cref="System.IO.IOException">Der Kanal ist gestört oder endete ohne Antwort.</exception>
    /// <exception cref="System.IO.InvalidDataException">Die Antwort ist keine Bestätigung.</exception>
    /// <exception cref="UnauthorizedAccessException">Der Kanal gehört einem anderen Benutzer.</exception>
    Task SendAsync(string? absolutePath, CancellationToken cancellationToken);
}
