using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MdExplorer.Core;
using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Services;

/// <summary>
/// Persistiert das <see cref="UiLayout"/> (Spaltenbreiten, Hilfefenster, Tag-Cloud, linker Tab,
/// Graph-Filter) als JSON in <c>%LOCALAPPDATA%\MdExplorer\ui-layout.json</c>. Unlesbares JSON,
/// E/A-Fehler und verweigerter Zugriff führen beim Laden zu <see cref="UiLayout.Default"/>, damit
/// ein kaputtes Layout den Start nicht verhindert.
/// </summary>
/// <remarks>
/// Geschrieben wird atomar über <see cref="IFileSystem.WriteAllBytesAtomic"/>: erst eine
/// Zwischendatei, dann das Umbenennen. Ein Absturz beim Schließen hinterlässt damit den alten
/// oder den neuen Stand, nie eine halbe Datei.
/// </remarks>
internal sealed partial class UiSettingsStore : IUiLayoutStore
{
    /// <summary>Dateiname der gespeicherten Layout-Daten.</summary>
    public const string LayoutFileName = "ui-layout.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IFileSystem _fileSystem;
    private readonly string _filePath;
    private readonly ILogger<UiSettingsStore> _logger;

    /// <summary>Erzeugt den Store mit Standard-Pfad.</summary>
    public UiSettingsStore(IFileSystem fileSystem, ILogger<UiSettingsStore> logger)
        : this(fileSystem, Path.Combine(AppPaths.Default.ApplicationDataDirectory, LayoutFileName), logger)
    {
    }

    /// <summary>Erzeugt den Store mit explizitem Pfad, für Tests.</summary>
    internal UiSettingsStore(IFileSystem fileSystem, string filePath, ILogger<UiSettingsStore> logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _filePath = filePath;
        _logger = logger;
    }

    /// <summary>Anzeigepfad für die Statusleiste.</summary>
    public string StorageLocation => _filePath;

    /// <summary>Lädt das Layout und liefert <see cref="UiLayout.Default"/>, wenn nichts gespeichert ist.</summary>
    public UiLayout Load()
    {
        if (!_fileSystem.FileExists(_filePath))
        {
            return UiLayout.Default;
        }
        try
        {
            UiLayout? layout = JsonSerializer.Deserialize<UiLayout>(_fileSystem.ReadAllBytes(_filePath), JsonOptions);
            return layout ?? UiLayout.Default;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            LogLoadFailure(_logger, exception, _filePath);
            return UiLayout.Default;
        }
    }

    /// <summary>Speichert das Layout. E/A- und Rechtefehler landen im Protokoll statt beim Aufrufer.</summary>
    public void Save(UiLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        try
        {
            _fileSystem.WriteAllBytesAtomic(_filePath, JsonSerializer.SerializeToUtf8Bytes(layout, JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogSaveFailure(_logger, exception, _filePath);
        }
    }

    [LoggerMessage(EventId = 400, Level = LogLevel.Warning, Message = "UI-Layout konnte nicht geladen werden ({Path}).")]
    private static partial void LogLoadFailure(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 401, Level = LogLevel.Error, Message = "UI-Layout konnte nicht gespeichert werden ({Path}).")]
    private static partial void LogSaveFailure(ILogger logger, Exception exception, string path);
}
