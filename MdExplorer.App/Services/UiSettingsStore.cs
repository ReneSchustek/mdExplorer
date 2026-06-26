using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MdExplorer.Core;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Services;

/// <summary>
/// Persistiert UI-Settings (aktuell: Spaltenbreiten) als JSON in
/// <c>%LOCALAPPDATA%\MdExplorer\ui-layout.json</c>. Liest robust — bei Fehlern wird
/// <see cref="UiLayout.Default"/> zurückgegeben statt zu werfen, damit der Startup
/// nie wegen kaputter Settings scheitert.
/// </summary>
internal sealed partial class UiSettingsStore
{
    /// <summary>Dateiname der gespeicherten Layout-Daten.</summary>
    public const string LayoutFileName = "ui-layout.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly ILogger<UiSettingsStore> _logger;

    /// <summary>Erzeugt den Store mit Standard-Pfad.</summary>
    public UiSettingsStore(ILogger<UiSettingsStore> logger)
        : this(Path.Combine(AppPaths.GetApplicationDataDirectory(), LayoutFileName), logger)
    {
    }

    /// <summary>Erzeugt den Store mit explizitem Pfad — für Tests.</summary>
    internal UiSettingsStore(string filePath, ILogger<UiSettingsStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = filePath;
        _logger = logger;
    }

    /// <summary>Anzeigepfad für die Statusleiste.</summary>
    public string StorageLocation => _filePath;

    /// <summary>Lädt das Layout — liefert <see cref="UiLayout.Default"/>, wenn nichts gespeichert ist.</summary>
    public UiLayout Load()
    {
        if (!File.Exists(_filePath))
        {
            return UiLayout.Default;
        }
        try
        {
            using FileStream stream = File.OpenRead(_filePath);
            UiLayout? layout = JsonSerializer.Deserialize<UiLayout>(stream, JsonOptions);
            return layout ?? UiLayout.Default;
        }
        catch (JsonException exception)
        {
            LogLoadFailure(_logger, exception, _filePath);
            return UiLayout.Default;
        }
        catch (IOException exception)
        {
            LogLoadFailure(_logger, exception, _filePath);
            return UiLayout.Default;
        }
    }

    /// <summary>Speichert das Layout. Wirft niemals — Fehler werden geloggt.</summary>
    public void Save(UiLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        try
        {
            string? directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                _ = Directory.CreateDirectory(directory);
            }
            using FileStream stream = File.Create(_filePath);
            JsonSerializer.Serialize(stream, layout, JsonOptions);
        }
        catch (IOException exception)
        {
            LogSaveFailure(_logger, exception, _filePath);
        }
        catch (UnauthorizedAccessException exception)
        {
            LogSaveFailure(_logger, exception, _filePath);
        }
    }

    [LoggerMessage(EventId = 400, Level = LogLevel.Warning, Message = "UI-Layout konnte nicht geladen werden ({Path}).")]
    private static partial void LogLoadFailure(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 401, Level = LogLevel.Error, Message = "UI-Layout konnte nicht gespeichert werden ({Path}).")]
    private static partial void LogSaveFailure(ILogger logger, Exception exception, string path);
}
