using System.Globalization;
using System.Text.Json;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Hosting;
using MdExplorer.Core.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Core.Settings;

/// <summary>
/// JSON-basierte Implementierung von <see cref="ISettingsService"/>. Schreibt atomar über
/// <see cref="IFileSystem.WriteAllBytesAtomicAsync"/>, hält den aktuellen Stand im Speicher und
/// fängt korrupte Dateien mit Default-Stand ab.
/// </summary>
/// <remarks>
/// Laden und Speichern laufen unter derselben Sperre. Zwei überlappende Speichervorgänge (Dialog
/// und Kontextmenü des Ordnerbaums) sähen sonst denselben alten Stand, und Ereignis wie Verlauf
/// meldeten einen Vorher-Wert, den es nie gab.
/// </remarks>
public sealed partial class JsonSettingsService : ISettingsService, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = SettingsJson.Options;

    private readonly string _settingsFilePath;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<JsonSettingsService> _logger;
    private readonly ISettingsHistoryStore? _historyStore;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettings _current = AppSettings.Default;
    private bool _fileHasNewerSchema;
    private bool _disposed;

    /// <summary>Erzeugt den Service.</summary>
    /// <param name="settingsFilePath">Vollständiger Pfad der Einstellungsdatei.</param>
    /// <param name="fileSystem">Dateisystem für Lesen und atomares Schreiben.</param>
    /// <param name="logger">Protokoll.</param>
    /// <param name="historyStore">Verlauf für Schnappschüsse und Audit, optional.</param>
    /// <param name="timeProvider">Uhr für Zeitstempel im Verlauf und in Sicherungsnamen.</param>
    public JsonSettingsService(
        string settingsFilePath,
        IFileSystem fileSystem,
        ILogger<JsonSettingsService> logger,
        ISettingsHistoryStore? historyStore,
        TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _settingsFilePath = settingsFilePath;
        _fileSystem = fileSystem;
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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _current = await ReadFileAsync(cancellationToken).ConfigureAwait(false);
            return _current;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">Ein Zahlenwert liegt außerhalb seiner Grenzen.</exception>
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AppSettings normalized = Normalize(settings);
        ThrowIfOutOfBounds(normalized);

        // Vor dem ersten await: Aufrufer-Sync-Context erfassen, damit Subscriber später
        // wieder auf dem ursprünglichen Thread (z. B. WPF-UI-Dispatcher) zugestellt werden.
        SynchronizationContext? capturedContext = SynchronizationContext.Current;

        SavedChange? change;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            change = await PersistAsync(normalized, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _gate.Release();
        }

        if (change is not null)
        {
            RaiseSettingsChanged(capturedContext, change.Previous, normalized);
        }
    }

    /// <summary>Gibt die interne Sperre frei.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _gate.Dispose();
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
        // Schema-Version 3 führt CheckForUpdatesAtStartup ein. Fehlt das Feld in einer älteren
        // Datei, setzt die Deserialisierung den Vorgabewert true des Konstruktors ein.
        BehaviorSettings behavior = input.Behavior ?? BehaviorSettings.Default;

        IReadOnlyList<string> roots = indexing.Roots ?? [];
        IReadOnlyList<string> exclusions = indexing.ExclusionPatterns ?? IndexingSettings.DefaultExclusionPatterns;
        // Ältere Dateien kennen das Feld nicht, die Deserialisierung liefert null. Eine leere
        // Liste ist dann der richtige Stand: Ausschlüsse aus dem Kontextmenü gab es dort nicht.
        IReadOnlyList<string> uiExcluded = indexing.UiExcludedFolders ?? [];
        // Schema-Version 2 führt AutoExtractHashtags ein, ohne Vorgabewert im Konstruktor. Ältere
        // Dateien liefern deshalb false, obwohl Hashtags dort immer ausgewertet wurden.
        bool autoExtractHashtags = input.SchemaVersion < 2 || indexing.AutoExtractHashtags;

        return new AppSettings(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings(roots, exclusions, uiExcluded, autoExtractHashtags),
            appearance,
            behavior);
    }

    // Beim Laden wird ein Wert außerhalb still ersetzt, beim Speichern nicht: Wer speichert, hat
    // den Wert gerade selbst gesetzt, und ein stilles Ersetzen verdeckte den Fehler des Aufrufers.
    private static void ThrowIfOutOfBounds(AppSettings settings)
    {
        BoundedValue? violation = SettingsBounds.Of(settings).FirstOrDefault(bounded => !bounded.IsWithinBounds);
        if (violation is not null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                violation.Value,
                $"{violation.Field} muss zwischen {violation.Minimum} und {violation.Maximum} liegen.");
        }
    }

    private static AppSettings WithValue(AppSettings settings, SettingsField field, int value) => field switch
    {
        SettingsField.PreviewFontSize => settings with { Appearance = settings.Appearance with { PreviewFontSize = value } },
        SettingsField.ResultsPerPage => settings with { Appearance = settings.Appearance with { ResultsPerPage = value } },
        SettingsField.SearchDebounceMs => settings with { Behavior = settings.Behavior with { SearchDebounceMs = value } },
        SettingsField.IndexerResyncIntervalSeconds => settings with { Behavior = settings.Behavior with { IndexerResyncIntervalSeconds = value } },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Feld hat keine Zahlengrenze."),
    };

    [LoggerMessage(EventId = 606, Level = LogLevel.Warning, Message = "Einstellung {Field} = {Value} liegt außerhalb von {Minimum} bis {Maximum} — Vorgabe {Fallback} gilt.")]
    private static partial void LogValueOutOfRange(ILogger logger, SettingsField field, int value, int minimum, int maximum, int fallback);

    [LoggerMessage(EventId = 600, Level = LogLevel.Information, Message = "Settings-Datei nicht vorhanden — Defaults werden verwendet: {Path}")]
    private static partial void LogFileMissing(ILogger logger, string path);

    [LoggerMessage(EventId = 601, Level = LogLevel.Warning, Message = "Settings-Datei leer — Defaults werden verwendet: {Path}")]
    private static partial void LogFileEmpty(ILogger logger, string path);

    [LoggerMessage(EventId = 602, Level = LogLevel.Warning, Message = "Settings-Datei enthält ungültiges JSON — Defaults werden verwendet, die Datei ist gesichert unter {BackupPath}")]
    private static partial void LogJsonInvalid(ILogger logger, string backupPath, Exception exception);

    [LoggerMessage(EventId = 603, Level = LogLevel.Warning, Message = "Settings-Datei nicht lesbar — Defaults werden verwendet: {Path}")]
    private static partial void LogReadFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 604, Level = LogLevel.Information, Message = "Settings-Datei geladen (Schema {Schema}): {Path}")]
    private static partial void LogLoaded(ILogger logger, string path, int schema);

    [LoggerMessage(EventId = 605, Level = LogLevel.Information, Message = "Settings-Datei persistiert: {Path}")]
    private static partial void LogSaved(ILogger logger, string path);

    [LoggerMessage(EventId = 607, Level = LogLevel.Warning, Message = "Settings-Datei hat Schema {FileSchema}, diese Fassung kennt {KnownSchema}. Unbekannte Felder werden vor dem nächsten Speichern gesichert.")]
    private static partial void LogNewerSchema(ILogger logger, int fileSchema, int knownSchema);

    [LoggerMessage(EventId = 608, Level = LogLevel.Warning, Message = "Settings-Datei konnte nicht gesichert werden: {Path}")]
    private static partial void LogBackupFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 609, Level = LogLevel.Warning, Message = "Settings-Verlauf konnte nicht geschrieben werden; die Einstellungen selbst sind gespeichert.")]
    private static partial void LogHistoryFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 613, Level = LogLevel.Error, Message = "Ein Empfänger der Einstellungsänderung ist mit einem Fehler abgebrochen.")]
    private static partial void LogSubscriberFailed(ILogger logger, Exception exception);

    private async Task<AppSettings> ReadFileAsync(CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(_settingsFilePath))
        {
            LogFileMissing(_logger, _settingsFilePath);
            return AppSettings.Default;
        }

        try
        {
            byte[] bytes = await _fileSystem.ReadAllBytesAsync(_settingsFilePath, cancellationToken).ConfigureAwait(false);
            return InterpretFile(JsonSerializer.Deserialize<AppSettings>(bytes, SerializerOptions));
        }
        catch (JsonException ex)
        {
            // Die kaputte Datei bleibt erhalten: Beim nächsten Speichern würde sie sonst
            // überschrieben, und mit ihr alles, was der Nutzer von Hand eingetragen hatte.
            LogJsonInvalid(_logger, await BackupAsync("corrupt", cancellationToken).ConfigureAwait(false), ex);
            return AppSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReadFailed(_logger, _settingsFilePath, ex);
            return AppSettings.Default;
        }
    }

    private AppSettings InterpretFile(AppSettings? loaded)
    {
        if (loaded is null)
        {
            LogFileEmpty(_logger, _settingsFilePath);
            return AppSettings.Default;
        }
        _fileHasNewerSchema = loaded.SchemaVersion > AppSettings.CurrentSchemaVersion;
        if (_fileHasNewerSchema)
        {
            LogNewerSchema(_logger, loaded.SchemaVersion, AppSettings.CurrentSchemaVersion);
        }
        AppSettings result = WithinLimits(Normalize(loaded));
        LogLoaded(_logger, _settingsFilePath, loaded.SchemaVersion);
        return result;
    }

    /// <summary>
    /// Setzt Werte außerhalb ihrer Grenzen auf die Vorgabe zurück.
    /// </summary>
    /// <remarks>
    /// Eine von Hand bearbeitete Datei geht am Einstellungsdialog und seiner Prüfung vorbei. Die
    /// Vorgabe statt des nächsten Grenzwerts: Ein Wert außerhalb ist ein Versehen, und die
    /// Grenze ist selten das, was gemeint war.
    /// </remarks>
    private AppSettings WithinLimits(AppSettings settings)
    {
        AppSettings result = settings;
        foreach (BoundedValue bounded in SettingsBounds.Of(settings).Where(bounded => !bounded.IsWithinBounds))
        {
            LogValueOutOfRange(_logger, bounded.Field, bounded.Value, bounded.Minimum, bounded.Maximum, bounded.Fallback);
            result = WithValue(result, bounded.Field, bounded.Fallback);
        }
        return result;
    }

    // Läuft unter der Sperre. Liefert null, wenn sich inhaltlich nichts geändert hat.
    private async Task<SavedChange?> PersistAsync(AppSettings normalized, CancellationToken cancellationToken)
    {
        AppSettings previous = _current;
        string serializedPrevious = JsonSerializer.Serialize(previous, SerializerOptions);
        string serializedCurrent = JsonSerializer.Serialize(normalized, SerializerOptions);

        if (_fileHasNewerSchema)
        {
            _ = await BackupAsync("schema", cancellationToken).ConfigureAwait(false);
            _fileHasNewerSchema = false;
        }
        await _fileSystem.WriteAllBytesAtomicAsync(
                _settingsFilePath,
                JsonSerializer.SerializeToUtf8Bytes(normalized, SerializerOptions),
                cancellationToken)
            .ConfigureAwait(false);
        _current = normalized;

        // Records-Equality reicht nicht: AppSettings enthält IReadOnlyList<string>-Felder, deren
        // Default-Vergleich die Referenz prüft. Der Dialog erzeugt bei jedem OK neue Listen; ein
        // Record-Vergleich meldete jedes Speichern als Änderung samt Rescan und Schnappschuss.
        if (SettingsDiff.Compute(serializedPrevious, serializedCurrent).Count == 0)
        {
            return null;
        }
        LogSaved(_logger, _settingsFilePath);
        await RecordHistoryAsync(previous, normalized, serializedPrevious, serializedCurrent).ConfigureAwait(false);
        return new SavedChange(previous);
    }

    // Die Datei ist zu diesem Zeitpunkt schon ersetzt. Ein Fehler im Verlauf darf das nicht
    // nachträglich zum Fehlschlag machen: Der Aufrufer bekäme eine Ausnahme, und das Ereignis,
    // auf das Indexer und Oberfläche warten, käme nie. Deshalb auch ohne Abbruch-Token.
    private async Task RecordHistoryAsync(
        AppSettings previous,
        AppSettings current,
        string serializedPrevious,
        string serializedCurrent)
    {
        if (_historyStore is null)
        {
            return;
        }
        try
        {
            await _historyStore.RecordAsync(previous, current, serializedPrevious, serializedCurrent, _timeProvider.GetUtcNow(), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LogHistoryFailed(_logger, ex);
        }
    }

    // Legt eine Kopie der aktuellen Datei neben sie. Gelingt das nicht, bleibt die Datei, wie sie
    // ist; überschrieben wird sie erst beim nächsten Speichern.
    private async Task<string> BackupAsync(string reason, CancellationToken cancellationToken)
    {
        string stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture);
        string backupPath = $"{_settingsFilePath}.{reason}-{stamp}";
        try
        {
            byte[] bytes = await _fileSystem.ReadAllBytesAsync(_settingsFilePath, cancellationToken).ConfigureAwait(false);
            await _fileSystem.WriteAllBytesAtomicAsync(backupPath, bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogBackupFailed(_logger, backupPath, ex);
        }
        return backupPath;
    }

    /// <summary>
    /// Stellt das <see cref="SettingsChanged"/>-Event auf dem zuvor erfassten
    /// <see cref="SynchronizationContext"/> zu. Fehlt der Context (Hintergrunddienst, Test ohne
    /// Dispatcher), geht es über den ThreadPool, nie synchron auf dem fortsetzenden Worker.
    /// </summary>
    private void RaiseSettingsChanged(SynchronizationContext? context, AppSettings previous, AppSettings current)
    {
        EventHandler<SettingsChangedEventArgs>? handler = SettingsChanged;
        if (handler is null)
        {
            return;
        }

        NotificationState state = new(handler, this, new SettingsChangedEventArgs(previous, current));
        if (context is null)
        {
            _ = ThreadPool.QueueUserWorkItem(static carrier => carrier.Deliver(), state, preferLocal: false);
            return;
        }
        context.Post(static carrier => ((NotificationState)carrier!).Deliver(), state);
    }

    /// <summary>Der alte Stand eines tatsächlich geänderten Speichervorgangs.</summary>
    private sealed record SavedChange(AppSettings Previous);

    /// <summary>
    /// Trägt Handler, Absender und Ereignisdaten in einem Objekt zum Zielthread. Die statischen
    /// Lambdas oben fangen so nichts aus der Methode ein.
    /// </summary>
    private readonly record struct NotificationState(
        EventHandler<SettingsChangedEventArgs> Handler,
        JsonSettingsService Source,
        SettingsChangedEventArgs Args)
    {
        // Eine Ausnahme auf einem ThreadPool-Thread beendet den Prozess. Ein fehlerhafter
        // Empfänger soll das Protokoll füllen, nicht die Anwendung beenden.
        public void Deliver()
        {
            try
            {
                Handler(Source, Args);
            }
            catch (Exception ex) when (BackgroundServiceWatchdog.IsRecoverable(ex, CancellationToken.None))
            {
                LogSubscriberFailed(Source._logger, ex);
            }
        }
    }
}
