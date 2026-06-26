# Architektur

MdExplorer ist eine WPF-Desktopanwendung auf .NET 10 mit modularer
Struktur. Module sind eigene .NET-Klassenbibliotheken im Repo-Wurzel
(Flat-Layout) und werden in der `MdExplorer.slnx`-Solution geführt.

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
| `MdExplorer.App` | Core, Data, Graph, Indexer, Parser, Search, TagCloud |
| `MdExplorer.Core` | — (keine) |
| `MdExplorer.Data` | Core |
| `MdExplorer.Indexer` | Core |
| `MdExplorer.Parser` | Core |
| `MdExplorer.Search` | Core |
| `MdExplorer.Graph` | Core, Parser |
| `MdExplorer.TagCloud` | Core, Parser |

## Modul-Verantwortlichkeiten

### MdExplorer.App

WPF-Frontend mit MVVM-Trennung (`Views/`, `ViewModels/`, `Services/`,
`Commands/`). Hostet den .NET-Generic-Host mit DI-Container, registriert
Module über `Add*`-Erweiterungsmethoden und stellt den
`SettingsWindow`-Dialog, das Hauptfenster und das
`GraphWindow`-WebView2-Fenster bereit.

### MdExplorer.Core

Abstraktionen, Datenmodelle und Cross-Cutting-Services ohne externe
Abhängigkeiten außer dem .NET-BCL und `Microsoft.Extensions.*`:

- `Abstractions/` — `IFileSystem`, `ISettingsService`, Repository- und
  Query-Interfaces
- `Models/` — `AppSettings`, `MarkdownFile`, `Tag`, `MarkdownFileTag`,
  `MarkdownDocument`
- `Settings/` — `JsonSettingsService`, `SettingsValidator`,
  `MdIgnoreReader`
- `FileSystem/` — `LocalFileSystem` (BCL-Wrapper)
- `Text/` — `LineEndingDetector`, `Utf8Decoder`
- `AppPaths` — zentrale Pfade unterhalb `%LOCALAPPDATA%\MdExplorer\`

### MdExplorer.Data

EF Core / SQLite mit Migrations und Repository-Implementierungen.
Stellt den `AppDbContext` und FTS5-Schreibpfade bereit. Die Datenbank
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
- **Tests** für jedes Modul in `<Modul>.Tests/` auf xUnit-Basis.

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
[ Data: AppDbContext → SQLite-Tabellen + FTS5-Spiegel ]
       |
       v
[ Search ] <---- Suchfeld (App)
[ Graph  ] <---- Ansicht → Graph…
[ Tags   ] <---- TagCloud + Tag-Verwaltung
```
