# MdExplorer

[![CI](https://github.com/ReneSchustek/mdExplorer/actions/workflows/ci.yml/badge.svg)](https://github.com/ReneSchustek/mdExplorer/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)

Windows-Desktop-Werkzeug (WPF / .NET 10) zum Erkunden und Verarbeiten
von Markdown-Beständen. Drei-Panel-UI mit Datei-Browser, Volltext-Suche
und HTML-Vorschau auf Basis von WebView2.

## Voraussetzungen

- .NET 10 SDK
- [WebView2-Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
  (auf modernen Windows-Installationen vorinstalliert)
- SQLite — wird über das `Microsoft.Data.Sqlite`-Paket bezogen, keine
  separate Installation nötig

## Installation

```powershell
git clone <repository-url> MdExplorer
cd MdExplorer
dotnet restore MdExplorer.slnx
```

## Entwicklung

```powershell
dotnet build MdExplorer.slnx -c Release
dotnet test  MdExplorer.slnx
dotnet run   --project MdExplorer.App
```

### Statische Analyse

Produktivprojekte ziehen `StyleCop.Analyzers` und `SonarAnalyzer.CSharp`
zentral über `Directory.Build.props` ein; Tests bleiben ausgenommen.
Der Build läuft mit `TreatWarningsAsErrors=true` — jede StyleCop- oder
Sonar-Meldung bricht den Build. Regelwerk: `stylecop.json` und der
StyleCop/Sonar-Abschnitt in `.editorconfig`.

```powershell
dotnet build MdExplorer.slnx -warnaserror
```

liefert bei sauberem Stand 0 Warnungen / 0 Fehler.

Vollständige Anleitung: [`docs/HANDBUCH.md`](docs/HANDBUCH.md).

## Projektstruktur

Flat-Layout: alle Module liegen direkt unter dem Repo-Wurzelverzeichnis.

| Projekt | Inhalt |
|---------|--------|
| `MdExplorer.App` | WPF-Frontend (MVVM, WebView2-Preview, Settings-Dialog) |
| `MdExplorer.Core` | Abstraktionen, Pfade, `IFileSystem`, Settings-Modell |
| `MdExplorer.Data` | EF Core / SQLite, Repositories, Migrations |
| `MdExplorer.Indexer` | Datei-Scan, `FileSystemWatcher`, Hash-Pipeline |
| `MdExplorer.Parser` | Markdig-basierter Parser mit WikiLink-Erweiterung |
| `MdExplorer.Search` | FTS5-Suche über SQLite |
| `MdExplorer.Graph` | WikiLink-Graph (Snapshot + Canvas-Renderer) |
| `MdExplorer.TagCloud` | Tag-Cloud-UserControl + Hintergrund-Refresh |
| `*.Tests` | xUnit-Tests pro Modul |
| `tools/` | Build- und Sicherheits-Tooling (`gitleaks`, Secret-Scan) |
| `docs/` | Architektur- und Settings-Dokumentation |

Eine ausführliche Schichten- und Abhängigkeitsübersicht steht in
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Konfiguration

### Anwendungsdaten

| Datei | Pfad | Inhalt |
|-------|------|--------|
| `settings.json` | `%LOCALAPPDATA%\MdExplorer\settings.json` | Index-Roots, Ausschluss-Muster, Darstellung, Verhalten |
| `ui-layout.json` | `%LOCALAPPDATA%\MdExplorer\ui-layout.json` | Spaltenbreiten des Hauptfensters |
| `app.db` | `%LOCALAPPDATA%\MdExplorer\app.db` | SQLite-Datenbank (FTS5-Suchindex, Tags) |
| `logs\` | `%LOCALAPPDATA%\MdExplorer\logs\` | tägliche Log-Dateien |
| `settings-history\` | `%LOCALAPPDATA%\MdExplorer\settings-history\` | pro Save ein JSON-Snapshot (Retention 30) |
| `settings-audit.log` | `%LOCALAPPDATA%\MdExplorer\settings-audit.log` | JSON-Lines mit Wann/Snapshot/Diff je Save |

### Settings-Dialog

`Datei → Einstellungen…` oder Tastenkürzel `Strg+,` öffnet einen modalen
Dialog mit drei Tabs:

- **Indexierung** — Index-Wurzeln, Glob-Ausschluss-Muster
  (mit `!`-Negation), Auto-Hashtag-Extraktion
- **Darstellung** — Theme (System/Hell/Dunkel), Preview-Schriftgröße,
  Treffer pro Seite
- **Verhalten** — Such-Debounce, Indexer-Resync-Intervall

Speichern erfolgt atomar (`.tmp` + `File.Move`). Detailliertes Schema
und Beispiele: [`docs/SETTINGS.md`](docs/SETTINGS.md).

### Folder-Tree und Indexierung pausieren

Im Tab „Ordner" zeigt das Folder-Tree die konfigurierten Index-Wurzeln.
Rechtsklick auf einen Ordner öffnet ein Kontextmenü:

- **Indexierung pausieren** — fügt den Ordner zu `Indexing.UiExcludedFolders`
  in der `settings.json` hinzu. Der Ordner und seine Inhalte werden ab dem
  nächsten Indexer-Lauf übersprungen; im Tree bleibt der Knoten ausgegraut
  sichtbar (Pause-Icon).
- **Indexierung wieder aufnehmen** — entfernt den Pfad aus der Liste. Der
  nächste Indexer-Lauf nimmt die Dateien wieder auf.

Die Pause-Liste persistiert beim Neustart und wirkt additiv zu den
Glob-`ExclusionPatterns` und `.mdignore`-Dateien.

## Bedienung

### Hauptfenster

Drei-Panel-Layout mit optionaler Tag-Cloud rechts:

1. **Links** — Tab „Ordner" (Baum) oder „Alle Dateien" (flache Liste)
2. **Mitte** — Suchpanel mit Treffer-Liste
3. **Rechts** — Dokument-Panel (Lese-/Bearbeiten-Modus)
4. **Optional ganz rechts** — Tag-Cloud (ein-/ausblendbar)

### Tastatur-Shortcuts

| Shortcut | Aktion |
|----------|--------|
| `Strg+F` | Suchfeld fokussieren |
| `Esc` | Sucheingabe leeren |
| `Strg+,` | Einstellungen öffnen |
| `Strg+E` | Dokument-Panel zwischen Lesen und Bearbeiten umschalten |
| `Strg+S` | Aktuelle Markdown-Datei speichern (nur im Bearbeiten-Modus) |
| `F1` | Handbuch-Fenster öffnen |

### Suche

Das Suchpanel in der mittleren Spalte sucht beim Tippen — nach einem
konfigurierbaren Debounce feuert die FTS5-Volltextsuche automatisch;
ein expliziter Such-Button ist nicht nötig. Mehrere Wörter werden mit
„UND" verknüpft, `"…"` sucht Phrasen, `wort*` als Präfix; die Filter
`tag:`, `-tag:` und `path:` schränken auf Metadaten bzw. Pfad-Fragmente
ein. Über die Dropdowns lassen sich Modus (FTS5 / Regex) und
Ähnlichkeit (None / Stemmed / NearStem / NearStemSynonyms) umschalten.

Das Kontrollkästchen **„Nur aktueller Ordner"** steuert den Such-Scope:
ohne Haken (Default) wird global über alle Index-Wurzeln gesucht; mit
Haken werden die Treffer auf den im Ordnerbaum gewählten Pfad und
dessen Unterordner beschränkt. Die Wurzel selbst zu wählen hebt die
Einschränkung wieder auf. Umschalten löst die Suche sofort neu aus.

### Markdown-Editor

Im rechten Panel lässt sich jede `.md`-Datei direkt bearbeiten. Die
Mode-Umschaltung erfolgt über den Toolbar-Button „Bearbeiten" oder
`Strg+E`. Im Edit-Modus zeigt eine Monospace-TextBox den Rohtext; die
Tag-Leiste oben aktualisiert sich live nach 300 ms Debounce. Tags lassen
sich manuell hinzufügen, entfernen oder umbenennen — neue Tags werden
in einem verwalteten Kommentar-Block `<!-- mdexplorer-tags: #a #b -->`
am Dateiende gepflegt. `Strg+S` speichert atomar (Temp-Datei +
`File.Move`); das ursprüngliche Zeilenende (CRLF/LF) bleibt erhalten.
Externe Änderungen während einer Edit-Session werden vor dem Schreiben
erkannt und blockieren das Speichern bis zur manuellen Auflösung.

**Schreibschutz (Default):** Jede frisch geladene Datei ist gegen
versehentliches Editieren gesperrt — Tippen, Tag-Hinzufügen, Tag-
Entfernen und `Strg+S` bleiben wirkungslos. Der Button „🔒 Entsperren"
in der Editor-Toolbar gibt die Datei zum Bearbeiten frei; „🔓 Sperren"
schließt sie wieder ab.

**Direct-Load:** Klickt der Nutzer auf eine `.md`-Datei, die der
Indexer noch nicht erfasst hat (Erstkonfiguration, sehr große Roots),
lädt der Editor sie direkt vom Dateisystem statt den Klick zu
ignorieren. Die Preview rendert sofort; Speichern bleibt gesperrt,
bis der Indexer-Lauf die Datei erfasst hat.

### Tag-Cloud

`Ansicht → Tag-Cloud` blendet das rechte Panel ein. Es zeigt die
häufigsten Tags mit logarithmisch skalierter Schriftgröße. Klick setzt
das Suchfeld auf `tag:<slug>` und triggert eine Suche; `Strg` ergänzt
additiv, `Alt` exkludiert (`-tag:<slug>`). Sortierreihenfolge
(Häufigkeit / Alphabetisch / Zuletzt verwendet) und Long-Tail-Modus
lassen sich in der Panel-Kopfzeile umschalten.

### Tag-Verwaltung

`Ansicht → Tag-Verwaltung…` öffnet einen modalen Dialog, der alle Tags
mit Anzahl betroffener Dateien auflistet. Pro Auswahl:

- **Umbenennen** — alle Vorkommen `#alt` werden zu `#neu` (Body +
  YAML-Frontmatter `tags`-Listen).
- **Zusammenführen** — der Quell-Tag verschwindet; sein Vorkommen wird
  durch den Ziel-Tag ersetzt. Duplikate im Frontmatter entfernt der
  Dialog automatisch.
- **Löschen** — sämtliche Vorkommen werden aus Body und Frontmatter
  entfernt.

Vor jeder Operation zeigt ein Bestätigungsdialog die Anzahl betroffener
Dateien und die ersten zehn Pfade. Dateien werden atomar geschrieben;
der `FileSystemWatcher` triggert anschließend automatisch den Re-Index.

### Indexer-Fortschritt während des ersten Scans

Auf grossen Wurzeln (mehrere Tausend `.md`-Dateien) committed der Indexer den
Initial-Scan in Batches: nach jeweils `Indexer.InitialScanBatchSize` Dateien
(Default 100) erfolgt ein Zwischen-`SaveChanges`. Der „Alle Dateien"-Tab und
der Folder-Tree aktualisieren sich nach jedem Batch automatisch — der Tab
bleibt nicht mehr leer, bis der gesamte Scan durch ist.

### Betriebs-Status (Health-LED)

Links in der Statusleiste sitzt eine LED, die den aggregierten Betriebs-Status
zeigt: grün = normal, gelb = Warnungen im jüngsten Log-Fenster, rot = Fehler
oder Critical-Einträge. Der ToolTip zeigt den Grund (Anzahl + letzte Meldung).
Klick auf die LED öffnet direkt den Live-Log-Viewer.

### Live-Log-Viewer

`Ansicht → Logs…` öffnet ein eigenes Fenster mit den letzten Log-Einträgen aus
dem In-Memory-Ringpuffer (Kapazität 2000). Die Toolbar bietet einen
Minimum-Level-Filter (Alle bis Kritisch) und eine Substring-Suche über
Nachricht und Quelle. Der „Exportieren…"-Button schreibt die aktuell sichtbaren
Einträge als UTF-8-Datei. Der Sink läuft parallel zu File- und Debug-Sink — die
Rotation der `logs\`-Dateien bleibt davon unberührt.

### Settings-Audit-Trail

Jede Settings-Änderung erzeugt automatisch zwei Spuren:

- ein vollständiger JSON-Snapshot unter `settings-history\settings.<UTC>.json`
  (Retention 30, älteste werden verworfen)
- eine JSON-Lines-Zeile in `settings-audit.log` mit `timestamp`, `snapshot` und
  einem strukturellen `changes`-Diff (Pfad inkl. Array-Index, alte und neue
  Werte als JSON-Literal).

Identische Speichervorgänge (gleicher Stand) erzeugen weder Snapshot noch
Audit-Eintrag.

### WikiLink-Graph

`Ansicht → Graph…` öffnet ein eigenes Fenster mit dem WikiLink-Graphen
des aktuellen Bestands. Knoten sind Markdown-Dateien, Kanten sind
`[[WikiLink]]`-Referenzen aus dem Parser. Der Graph rendert in einer
eingebetteten WebView2-Instanz; HTML, JavaScript und CSS sind als
Embedded Resources im App-Modul abgelegt — keine externen CDNs,
keine Netzwerkverbindung notwendig. Die Content-Security-Policy
erzwingt `default-src 'none'` mit Nonce-basierter Skript-Whitelist.

### Update-Hinweis

Beim Start prüft die Anwendung einmal täglich, ob auf GitHub eine neuere
Version veröffentlicht wurde (öffentliche Releases-API, ohne Anmeldung).
Ist eine neuere Version verfügbar, erscheint oben im Hauptfenster eine
dezente, schließbare Hinweisleiste mit Link auf die Release-Seite — es wird
nichts automatisch heruntergeladen oder installiert. Die Prüfung lässt sich
unter `Datei → Einstellungen… → Verhalten` mit „Beim Start nach Updates
suchen" abschalten; ohne Netzverbindung verhält sich die Anwendung
unverändert.

## Lizenz

Veröffentlicht unter der [MIT-Lizenz](LICENSE) — © 2026 Rene Schustek.
