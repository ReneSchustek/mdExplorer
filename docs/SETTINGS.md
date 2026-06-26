# Settings

Alle Benutzereinstellungen liegen in einer einzigen JSON-Datei
unterhalb `%LOCALAPPDATA%\MdExplorer\settings.json`. Schreibvorgänge
sind atomar (`.tmp` + `File.Move`); fehlt die Datei oder ist sie
korrupt, lädt die Anwendung die Default-Werte und überschreibt sie
beim nächsten Speichern.

## Schema

Aktuelle Version: `2`.

```json
{
  "schemaVersion": 2,
  "indexing": {
    "roots": [
      "C:\\Users\\me\\Documents\\Notes",
      "D:\\Wiki"
    ],
    "exclusionPatterns": [
      "**/.git/**",
      "**/node_modules/**",
      "**/bin/**",
      "**/obj/**",
      "**/.vs/**"
    ],
    "uiExcludedFolders": [
      "C:\\Users\\me\\Documents\\Notes\\Archive\\2019"
    ],
    "autoExtractHashtags": true
  },
  "appearance": {
    "theme": "System",
    "previewFontSize": 16,
    "resultsPerPage": 50
  },
  "behavior": {
    "searchDebounceMs": 300,
    "indexerResyncIntervalSeconds": 300
  }
}
```

## Felder

### `schemaVersion`

Versionsnummer des persistierten Schemas. Wird beim Laden gegen
`AppSettings.CurrentSchemaVersion` geprüft; bei Schema-Drift werden
fehlende Felder mit Defaults aufgefüllt.

### `indexing`

| Feld | Typ | Default | Beschreibung |
|------|-----|---------|--------------|
| `roots` | `string[]` | `[]` | Absolute Wurzelpfade, die rekursiv nach `*.md` durchsucht werden |
| `exclusionPatterns` | `string[]` | siehe unten | Glob-Muster für Ausschlüsse, mit `!`-Negation als Ausnahme |
| `uiExcludedFolders` | `string[]` | `[]` | Absolute Pfade einzelner Ordner, die per Folder-Tree-Kontextmenü pausiert wurden |
| `autoExtractHashtags` | `bool` | `true` | Wenn `true`, extrahiert der Indexer Hashtags aus dem Markdown-Body. Frontmatter-`tags` werden unabhängig davon stets übernommen |

**Default-Ausschlussmuster:**

```json
[
  "**/.git/**",
  "**/node_modules/**",
  "**/bin/**",
  "**/obj/**",
  "**/.vs/**"
]
```

### `appearance`

| Feld | Typ | Default | Bereich | Beschreibung |
|------|-----|---------|---------|--------------|
| `theme` | `enum` | `System` | `System` \| `Light` \| `Dark` | Theme-Wahl |
| `previewFontSize` | `int` | `16` | 8–64 | Schriftgröße der HTML-Preview in Pixel |
| `resultsPerPage` | `int` | `50` | 10–1000 | Anzahl angezeigter Suchtreffer |

### `behavior`

| Feld | Typ | Default | Bereich | Beschreibung |
|------|-----|---------|---------|--------------|
| `searchDebounceMs` | `int` | `300` | 50–5000 | Wartezeit nach letztem Tastendruck, bevor die Suche feuert |
| `indexerResyncIntervalSeconds` | `int` | `300` | 0–3600 | Intervall für den Soll/Ist-Abgleich des Indexers (`0` deaktiviert) |

## Glob-Muster und Negation

`exclusionPatterns` nutzt
[`Microsoft.Extensions.FileSystemGlobbing`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.filesystemglobbing).
Pfade werden relativ zum jeweiligen Index-Root und mit
Vorwärts-Slashes gematcht.

**Syntax:**

| Muster | Bedeutung |
|--------|-----------|
| `*.md` | Alle `.md`-Dateien direkt im Root |
| `**/*.md` | Alle `.md`-Dateien rekursiv |
| `**/drafts/**` | Alles unterhalb eines `drafts`-Ordners auf beliebiger Tiefe |
| `archive/**` | Alles unterhalb des `archive`-Ordners im Root |
| `!muster` | **Negation** — hebt einen Ausschluss wieder auf (Ausnahme) |

**Semantik der Negation:** Ein Muster ohne `!` schließt Dateien aus,
ein Muster mit `!` macht eine Ausnahme von einem zuvor passenden
Ausschluss. Reihenfolge spielt keine Rolle: Negation gewinnt immer,
sobald sie matched.

**Beispiel:** Alles unterhalb `archive/` ausschließen, außer
`archive/2026/`:

```json
"exclusionPatterns": [
  "**/.git/**",
  "**/node_modules/**",
  "archive/**",
  "!archive/2026/**"
]
```

**Beispiel:** Nur `README.md`-Dateien indizieren, alles andere
ausschließen:

```json
"exclusionPatterns": [
  "**/*",
  "!**/README.md"
]
```

## Zusätzliche Ausschluss-Quellen

Neben `exclusionPatterns` aus der `settings.json` wirken zwei weitere
Mechanismen **additiv** (eine Datei gilt als ausgeschlossen, sobald
**eine** Quelle sie matched):

1. **`.mdignore`-Dateien** — werden hierarchisch aus dem Index-Root
   in den jeweiligen Ordnerbaum gelesen. Syntax identisch zu
   `exclusionPatterns`. Praktisch für projekt-spezifische
   Ausschlüsse, die nicht zentral in der `settings.json` stehen sollen.
2. **`uiExcludedFolders`** — absolute Pfade, die per Rechtsklick im
   Folder-Tree pausiert wurden. Wirken via Pfad-Präfix (alle Dateien
   unterhalb des Pfades), nicht via Glob.

## Tabs im Settings-Dialog

`Datei → Einstellungen…` oder `Strg+,` öffnet einen modalen Dialog
mit drei Tabs:

- **Indexierung** — `roots`, `exclusionPatterns`, `autoExtractHashtags`
- **Darstellung** — `theme`, `previewFontSize`, `resultsPerPage`
- **Verhalten** — `searchDebounceMs`, `indexerResyncIntervalSeconds`

`Abbrechen` verwirft Änderungen, `Anwenden` schreibt sie atomar und
löst die abhängigen Services über das `SettingsChanged`-Event aus
(z. B. Indexer-Re-Scan, Glob-Matcher-Cache-Invalidierung).

## Migration

Beim Laden ruft `JsonSettingsService.Normalize` die folgenden
Migrations-Schritte auf:

- Fehlende Sub-Objekte (`indexing`, `appearance`, `behavior`) werden
  mit deren Default-Werten ersetzt.
- Fehlt `uiExcludedFolders` (Pre-Schema-2-Dateien), wird eine leere
  Liste angenommen — bestehende UI-Pausen sind dann nicht
  rekonstruierbar und müssen neu gesetzt werden.
- Schema-Version `< 2` setzt `autoExtractHashtags = true`, um die
  bisherige Default-Semantik zu erhalten.

Die normalisierte Instanz wird beim nächsten Schreiben unter
`schemaVersion: 2` persistiert.
