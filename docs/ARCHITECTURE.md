# Architektur

MdExplorer ist eine WPF-Desktopanwendung auf .NET 10 mit modularer
Struktur. Jedes Modul ist eine eigene .NET-Klassenbibliothek; sie liegen
unter `src/`, die zugehörigen Testprojekte unter `tests/`. Geführt werden
alle in der `MdExplorer.slnx`-Solution.

## Schichten

```
+-----------------------------------------------------------+
|  Presentation                                             |
|    MdExplorer.App  (WPF, MVVM, WebView2)                  |
+-----------------------------------------------------------+
|  Domain-Module                                            |
|    Indexer | Parser | Search | Graph | TagCloud | Data    |
+-----------------------------------------------------------+
|  Foundation                                               |
|    MdExplorer.Core                                        |
|      Abstraktionen (IFileSystem, ISettingsService, …)     |
|      Modelle (AppSettings, MarkdownFile, Tag, …)          |
|      Pfade (AppPaths → %LOCALAPPDATA%\MdExplorer)         |
+-----------------------------------------------------------+
```

Die Foundation kennt **niemand außer** Domain-Module und App; sie hat
selbst keine Projekt-Referenzen. Domain-Module kennen Core und ggf.
Parser, aber **untereinander nichts**. Cross-Modul-Kommunikation läuft
ausschließlich über Abstraktionen in `MdExplorer.Core.Abstractions`.

## Modul-Abhängigkeitsgraph

```
                    +-------------------+
                    |   MdExplorer.App  |
                    +-------------------+
                      |    |    |    |    |    |    |
       +--------------+    |    |    |    |    |    +--------------+
       |                   |    |    |    |    |                   |
       v                   v    |    |    |    v                   v
  +---------+        +---------+|    |    |+----------+      +-----------+
  | Indexer |        |  Data   ||    |    || TagCloud |      |   Graph   |
  +---------+        +---------+|    |    |+----------+      +-----------+
       \                 \      |    |    |     /                  /
        \                 \     v    v    v    /                  /
         \                 \  +--------+      /                  /
          \                 \ | Search |     /                  /
           \                 \+--------+    /                  /
            \                 \    \       /                  /
             \                 \    \     /                  /
              \                 \    \   /  +---------+     /
               \                 \    \ /   | Parser  | <--+
                \                 \    v    +---------+
                 \                 \   |        |
                  v                 v  v        v
                  +-------------------------+
                  |     MdExplorer.Core     |
                  +-------------------------+
```

| Modul | Projekt-Referenzen |
|-------|--------------------|
| `MdExplorer.App` | Core, Data, Graph, Indexer, Parser, Search, TagCloud, Update |
| `MdExplorer.Core` | — (keine) |
| `MdExplorer.Data` | Core |
| `MdExplorer.Indexer` | Core |
| `MdExplorer.Parser` | Core |
| `MdExplorer.Search` | Core |
| `MdExplorer.Graph` | Core, Parser |
| `MdExplorer.TagCloud` | Core, Parser |
| `MdExplorer.Update` | Core |

## Modul-Verantwortlichkeiten

### MdExplorer.App

WPF-Frontend mit MVVM-Trennung (`Views/`, `ViewModels/`, `Services/`,
`Controls/`, `Converters/`). Hostet den .NET-Generic-Host mit DI-Container,
registriert Module über `Add*`-Erweiterungsmethoden und stellt den
`SettingsWindow`-Dialog, das Hauptfenster und das
`GraphWindow`-WebView2-Fenster bereit.

Die Gestaltungslinie liegt in `Themes/`: `Tokens.xaml` trägt Abstände, Radien
und Typografie, `Light.xaml` und `Dark.xaml` je eine vollständige Farbbelegung
mit demselben Schlüsselsatz, `ControlStyles.xaml` die Grundbelegung der
Bedienelemente. Getauscht wird zur Laufzeit genau ein Wörterbuch — die Belegung.
Wiederverwendbare Bausteine (Suchfeld, Leerzustand, Sprungleiste) stehen als
Steuerelemente in `Controls/`, nicht als abgeschriebenes Markup.

### MdExplorer.Core

Abstraktionen, Datenmodelle und Cross-Cutting-Services ohne externe
Abhängigkeiten außer dem .NET-BCL und `Microsoft.Extensions.*`:

- `Abstractions/` — `IFileSystem`, `ISettingsService`, Repository- und
  Query-Interfaces
- `Models/` — `AppSettings`, `MarkdownFile`, `Tag`, `MarkdownFileTag`,
  `MarkdownDocument`, `ParseFailure`
- `Settings/` — `JsonSettingsService`, `SettingsValidator`,
  `MdIgnoreReader`
- `FileSystem/` — `LocalFileSystem` (BCL-Wrapper)
- `Text/` — `LineEndingDetector`, `Utf8Decoder`
- `Diagnostics/` — `ParseFailureStatus` (Zahl der nicht verarbeitbaren Dateien
  für die Betriebsanzeige)
- `AppPaths` — zentrale Pfade unterhalb `%LOCALAPPDATA%\MdExplorer\`

### MdExplorer.Data

EF Core / SQLite mit Migrations und Repository-Implementierungen.
Stellt den `MdExplorerDbContext` und FTS5-Schreibpfade bereit. Die Datenbank
liegt unter `%LOCALAPPDATA%\MdExplorer\app.db`.

### MdExplorer.Indexer

Datei-Scan mit `FileSystemWatcher`, Hash-Pipeline und Re-Sync-Loop.
Respektiert die Konfiguration aus `ISettingsService` (Roots,
Glob-`ExclusionPatterns`, `UiExcludedFolders`) sowie `.mdignore`-Dateien.

### MdExplorer.Parser

Markdig-basierter Parser mit eigener WikiLink-Extension (`[[ziel]]`),
Frontmatter-Reader und Hashtag-Extraktor.

### MdExplorer.Search

FTS5-Suche über SQLite mit Tokenizer-Konfiguration, Suchgewichtungen
und Highlight-Generierung.

### MdExplorer.Graph

Liefert einen `GraphSnapshot` aus den WikiLink-Beziehungen, serialisiert
ihn über `GraphJsonBuilder` und wird im App-Modul vom
`GraphWindow`-WebView2-Renderer konsumiert.

### MdExplorer.TagCloud

Hintergrund-Aggregation der Tag-Frequenzen mit `ObservableCollection`
und WPF-Thread-Synchronisierung (`EnableCollectionSynchronization`).

### MdExplorer.Update

Prüft über die GitHub-Releases-API auf neue Fassungen, lädt das
Installationspaket und gleicht es gegen den veröffentlichten SHA-256 ab.
Ohne Prüfwert wird nicht geladen, bei Abweichung die Datei verworfen statt
ausgeführt — der Installer ist unsigniert, die Prüfsumme ist der einzige Beleg.

## Querschnittsregeln

- **Keine Cross-Modul-Kopplung** außer über `MdExplorer.Core.Abstractions`.
- **Atomare Schreibvorgänge** für `settings.json`, `ui-layout.json` und
  Markdown-Dateien (`.tmp` + `File.Move`).
- **Asynchrone I/O** durchgehend (`async`/`await`,
  `ConfigureAwait(false)` in Bibliotheksprojekten).
- **DI-Registrierung** je Modul über `Add<Modul>()`-Erweiterung in
  `DependencyInjection/`.
- **Logging** über `Microsoft.Extensions.Logging` mit `LoggerMessage`-
  Source-Generators (Event-IDs pro Bereich).
- **Tests** für jedes Modul in `tests/<Modul>.Tests/` auf xUnit-Basis.

## Datenfluss: Datei → Index → Suche

```
[Markdown-Datei]
       |
       v
[ FileSystemWatcher (Indexer) ]
       |
       v
[ Parser: Frontmatter + Body + WikiLinks + #Hashtags ]
       |
       v
[ Data: MdExplorerDbContext → SQLite-Tabellen + FTS5-Spiegel ]
       |
       v
[ Search ] <---- Suchfeld (App)
[ Graph  ] <---- Ansicht → Graph…
[ Tags   ] <---- TagCloud + Tag-Verwaltung
```
