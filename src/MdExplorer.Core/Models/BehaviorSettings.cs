using System.ComponentModel.DataAnnotations;

namespace MdExplorer.Core.Models;

/// <summary>Settings für Verhalten (Such-Debounce, Indexer-Re-Sync-Intervall, Update-Prüfung).</summary>
/// <param name="SearchDebounceMs">Wartezeit nach letztem Tastendruck, bevor die Suche feuert.</param>
/// <param name="IndexerResyncIntervalSeconds">Soll/Ist-Abgleich des Indexers in Sekunden (<c>0</c> deaktiviert).</param>
/// <param name="CheckForUpdatesAtStartup">Wenn <see langword="true"/>, prüft die Anwendung beim Start auf neue Versionen.</param>
/// <param name="LoadRemoteImagesInPreview">
/// Wenn <see langword="true"/>, lädt die Vorschau auch Bilder aus dem Netz.
/// </param>
/// <remarks>
/// <para>
/// Die Grenzen werden wie bei <see cref="AppearanceSettings"/> vor dem Speichern und beim Laden
/// geprüft.
/// </para>
/// <para>
/// <c>LoadRemoteImagesInPreview</c> ist ab Werk <see langword="false"/>, und das ist eine Zusage, keine Vorsichtsmaßnahme: Die
/// Anwendung arbeitet vollständig ohne Internetverbindung. Ein Bild aus dem Netz in einer
/// Notiz würde beim Öffnen der Vorschau einen fremden Server aufrufen und ihm damit
/// verraten, wann welche Notiz angesehen wurde.
/// </para>
/// <para>
/// Wer die Abzeichen eines README sehen will, schaltet es ein. Dann steht in der Vorschau,
/// was im Dokument steht, samt der Anfragen, die dazugehören.
/// </para>
/// </remarks>
public sealed record BehaviorSettings(
    [property: Range(BehaviorSettings.MinSearchDebounceMs, BehaviorSettings.MaxSearchDebounceMs)] int SearchDebounceMs,
    [property: Range(BehaviorSettings.MinIndexerResyncIntervalSeconds, BehaviorSettings.MaxIndexerResyncIntervalSeconds)] int IndexerResyncIntervalSeconds,
    bool CheckForUpdatesAtStartup = true,
    bool LoadRemoteImagesInPreview = false)
{
    /// <summary>Kürzeste Wartezeit nach dem letzten Tastendruck in Millisekunden.</summary>
    public const int MinSearchDebounceMs = 50;

    /// <summary>Längste Wartezeit nach dem letzten Tastendruck; fünf Sekunden fühlen sich schon wie ein Hänger an.</summary>
    public const int MaxSearchDebounceMs = 5_000;

    /// <summary>Kleinstes Abgleich-Intervall in Sekunden; <c>0</c> schaltet den Abgleich ab.</summary>
    public const int MinIndexerResyncIntervalSeconds = 0;

    /// <summary>Größtes Abgleich-Intervall in Sekunden, eine Stunde.</summary>
    public const int MaxIndexerResyncIntervalSeconds = 3_600;

    /// <summary>
    /// Default-Wartezeit nach letztem Tastendruck in Millisekunden. Sie liegt über dem Abstand
    /// zweier Anschläge beim zügigen Tippen, die Suche läuft also erst in der Pause nach einem Wort.
    /// </summary>
    private const int DefaultSearchDebounceMs = 300;

    /// <summary>
    /// Default-Intervall für den Indexer-Re-Sync in Sekunden. Der Abgleich holt nach, was der
    /// Dateiwächter verpasst hat; fünf Minuten begrenzen, wie lange so eine Lücke stehen bleibt.
    /// </summary>
    private const int DefaultIndexerResyncIntervalSeconds = 300;

    /// <summary>300 ms Such-Debounce, 300 s Indexer-Resync, Update-Prüfung aktiv.</summary>
    public static BehaviorSettings Default => new(DefaultSearchDebounceMs, DefaultIndexerResyncIntervalSeconds, true);
}
