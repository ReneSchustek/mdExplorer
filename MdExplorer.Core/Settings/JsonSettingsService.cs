using System.Text.Json;
using System.Text.Json.Serialization;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Core.Settings;

/// <summary>
/// JSON-basierte Implementierung von <see cref="ISettingsService"/>. Persistiert die Datei
/// atomar (Schreiben in <c>.tmp</c>, danach <see cref="File.Move(string, string, bool)"/>),
/// hält den aktuellen Stand im Speicher und fängt korrupte Dateien mit Default-Stand ab.
/// </summary>
public sealed partial class JsonSettingsService : ISettingsService, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _settingsFilePath;
    private readonly ILogger<JsonSettingsService> _logger;
    private readonly ISettingsHistoryStore? _historyStore;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private AppSettings _current = AppSettings.Default;
    private bool _disposed;

    /// <summary>Erzeugt den Service. Der Pfad wird als Singleton-Konfiguration injiziert.</summary>
    public JsonSettingsService(string settingsFilePath, ILogger<JsonSettingsService> logger)
        : this(settingsFilePath, logger, historyStore: null, timeProvider: TimeProvider.System)
    {
    }

    /// <summary>
    /// Erzeugt den Service mit explizitem <see cref="ISettingsHistoryStore"/> für den
    /// Audit-Trail und <see cref="TimeProvider"/> für deterministische Tests.
    /// </summary>
    public JsonSettingsService(
        string settingsFilePath,
        ILogger<JsonSettingsService> logger,
        ISettingsHistoryStore? historyStore,
        TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _settingsFilePath = settingsFilePath;
        _logger = logger;
        _historyStore = historyStore;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public AppSettings Current => _current;

    /// <inheritdoc />
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    /// <inheritdoc />
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsFilePath))
        {
            LogFileMissing(_logger, _settingsFilePath);
            _current = AppSettings.Default;
            return _current;
        }

        try
        {
            FileStream stream = File.Open(
                _settingsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            AppSettings? loaded;
            await using (stream.ConfigureAwait(false))
            {
                loaded = await JsonSerializer
                    .DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (loaded is null)
            {
                LogFileEmpty(_logger, _settingsFilePath);
                _current = AppSettings.Default;
                return _current;
            }
            _current = Normalize(loaded);
            LogLoaded(_logger, _settingsFilePath, _current.SchemaVersion);
            return _current;
        }
        catch (JsonException ex)
        {
            LogJsonInvalid(_logger, _settingsFilePath, ex);
            _current = AppSettings.Default;
            return _current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReadFailed(_logger, _settingsFilePath, ex);
            _current = AppSettings.Default;
            return _current;
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        AppSettings previous = _current;
        AppSettings normalized = Normalize(settings);

        // Vor dem ersten await: Aufrufer-Sync-Context erfassen, damit Subscriber später
        // wieder auf dem ursprünglichen Thread (z. B. WPF-UI-Dispatcher) zugestellt werden.
        // Captive-Subscribers — etwa ObservableCollections an WPF-CollectionViews — würden
        // sonst auf einem ThreadPool-Worker mutiert (NotSupportedException).
        SynchronizationContext? capturedContext = SynchronizationContext.Current;

        string serializedCurrent = JsonSerializer.Serialize(normalized, SerializerOptions);
        string serializedPrevious = JsonSerializer.Serialize(previous, SerializerOptions);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string directory = Path.GetDirectoryName(_settingsFilePath)
                ?? throw new InvalidOperationException("Settings-Pfad hat kein Verzeichnis.");
            _ = Directory.CreateDirectory(directory);

            string tempPath = _settingsFilePath + ".tmp";
            FileStream writeStream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await using (writeStream.ConfigureAwait(false))
            {
                await JsonSerializer
                    .SerializeAsync(writeStream, normalized, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await writeStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(tempPath, _settingsFilePath, overwrite: true);
        }
        finally
        {
            _ = _writeLock.Release();
        }

        _current = normalized;

        // Records-Equality reicht hier nicht: AppSettings enthaelt IReadOnlyList<string>-
        // Felder, deren Default-Vergleich Reference-Equality verwendet. Der Settings-Dialog
        // erzeugt bei jedem OK-Klick neue Listen-Instanzen — die alte Pruefung schlug
        // dadurch jedes Mal an, obwohl inhaltlich nichts geaendert war, und triggerte
        // unnoetigen Full-Rescan sowie Audit-Snapshot bei jedem Save (Befund 2026-06-10).
        IReadOnlyList<SettingsChangeEntry> changes =
            SettingsDiff.Compute(serializedPrevious, serializedCurrent);
        if (changes.Count == 0)
        {
            return;
        }

        LogSaved(_logger, _settingsFilePath);
        if (_historyStore is not null)
        {
            // Audit-Trail: Snapshot + Diff schreiben. Bewusst nach dem Move,
            // damit ein History-Fehlschlag den eigentlichen Settings-Save nicht maskiert.
            await _historyStore.RecordAsync(
                    previous,
                    normalized,
                    serializedPrevious,
                    serializedCurrent,
                    _timeProvider.GetUtcNow(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        RaiseSettingsChanged(capturedContext, previous, normalized);
    }

    /// <summary>
    /// Stellt das <see cref="SettingsChanged"/>-Event auf dem zuvor erfassten
    /// <see cref="SynchronizationContext"/> zu. Fehlt der Context (z. B. Server-Background-Worker
    /// oder Unit-Tests ohne Dispatcher), wird auf den ThreadPool ausgewichen — niemals
    /// synchron auf dem fortsetzenden Worker, damit kein UI-Subscriber im falschen Thread läuft.
    /// </summary>
    private void RaiseSettingsChanged(
        SynchronizationContext? context,
        AppSettings previous,
        AppSettings current)
    {
        EventHandler<SettingsChangedEventArgs>? handler = SettingsChanged;
        if (handler is null)
        {
            return;
        }

        SettingsChangedEventArgs args = new(previous, current);
        NotificationState state = new(handler, this, args);

        if (context is null)
        {
            _ = ThreadPool.QueueUserWorkItem(
                static carrier => carrier.Handler(carrier.Source, carrier.Args),
                state,
                preferLocal: false);
            return;
        }

        context.Post(
            static carrier =>
            {
                NotificationState payload = (NotificationState)carrier!;
                payload.Handler(payload.Source, payload.Args);
            },
            state);
    }

    private readonly record struct NotificationState(
        EventHandler<SettingsChangedEventArgs> Handler,
        JsonSettingsService Source,
        SettingsChangedEventArgs Args);

    /// <summary>Gibt das interne <see cref="SemaphoreSlim"/> frei.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _writeLock.Dispose();
    }

    /// <summary>
    /// Setzt fehlende Felder auf Defaults zurück und behebt Schema-Drift. Wird sowohl
    /// beim Laden als auch beim Speichern angewandt, damit der gespeicherte Stand
    /// immer mit der aktuellen Schema-Version kompatibel ist.
    /// </summary>
    private static AppSettings Normalize(AppSettings input)
    {
        IndexingSettings indexing = input.Indexing ?? IndexingSettings.Default;
        AppearanceSettings appearance = input.Appearance ?? AppearanceSettings.Default;
        BehaviorSettings behaviorRaw = input.Behavior ?? BehaviorSettings.Default;
        // Schema-Version 3 fuehrt CheckForUpdatesAtStartup ein. Aeltere Dateien liefern
        // Default(bool)=false aus der Deserialisierung, was den dokumentierten Standard
        // (Pruefung aktiv) verletzen wuerde. Fuer Schema < 3 daher explizit auf true.
        bool checkForUpdates = input.SchemaVersion < 3 || behaviorRaw.CheckForUpdatesAtStartup;
        BehaviorSettings behavior = behaviorRaw with { CheckForUpdatesAtStartup = checkForUpdates };

        IReadOnlyList<string> roots = indexing.Roots ?? [];
        IReadOnlyList<string> exclusions = indexing.ExclusionPatterns ?? IndexingSettings.DefaultExclusionPatterns;
        // Legacy-Settings kennen das Feld noch nicht — Deserialisierung liefert null.
        // Wir fallen still auf eine leere Liste zurueck; der Nutzer behaelt seinen bisherigen Stand
        // und kann ueber das Folder-Tree-Kontextmenue neue UI-Ausschluesse erzeugen.
        IReadOnlyList<string> uiExcluded = indexing.UiExcludedFolders ?? [];
        // Schema-Version 2 hat AutoExtractHashtags. Aeltere Dateien liefern Default(bool)=false
        // aus der Deserialisierung, was die bisherige Auto-Tagging-Semantik (immer aktiv) verletzen wuerde.
        // Wir setzen daher fuer Schema-Versionen < 2 den dokumentierten Standard true.
        bool autoExtractHashtags = input.SchemaVersion < 2
            || indexing.AutoExtractHashtags;

        return new AppSettings(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings(roots, exclusions, uiExcluded, autoExtractHashtags),
            appearance,
            behavior);
    }

    [LoggerMessage(EventId = 600, Level = LogLevel.Information, Message = "Settings-Datei nicht vorhanden — Defaults werden verwendet: {Path}")]
    private static partial void LogFileMissing(ILogger logger, string path);

    [LoggerMessage(EventId = 601, Level = LogLevel.Warning, Message = "Settings-Datei leer — Defaults werden verwendet: {Path}")]
    private static partial void LogFileEmpty(ILogger logger, string path);

    [LoggerMessage(EventId = 602, Level = LogLevel.Warning, Message = "Settings-Datei enthält ungültiges JSON — Defaults werden verwendet: {Path}")]
    private static partial void LogJsonInvalid(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 603, Level = LogLevel.Warning, Message = "Settings-Datei nicht lesbar — Defaults werden verwendet: {Path}")]
    private static partial void LogReadFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 604, Level = LogLevel.Information, Message = "Settings-Datei geladen (Schema {Schema}): {Path}")]
    private static partial void LogLoaded(ILogger logger, string path, int schema);

    [LoggerMessage(EventId = 605, Level = LogLevel.Information, Message = "Settings-Datei persistiert: {Path}")]
    private static partial void LogSaved(ILogger logger, string path);
}
