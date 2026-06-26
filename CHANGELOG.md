# Changelog

Alle nennenswerten Änderungen an diesem Projekt werden in dieser Datei
dokumentiert. Das Format orientiert sich an
[Keep a Changelog](https://keepachangelog.com/de/1.1.0/); die Versionierung
folgt [Semantic Versioning](https://semver.org/lang/de/).

## [Unveröffentlicht]

### Hinzugefügt
- Update-Prüfung beim Start: Die Anwendung prüft einmal täglich über die
  GitHub-Releases-API auf neue Versionen und blendet bei Verfügbarkeit eine
  schließbare Hinweisleiste mit Link auf die Release-Seite ein. Abschaltbar
  unter `Einstellungen → Verhalten`.
- Such-Scope „Nur aktueller Ordner": Die Trefferliste lässt sich wahlweise
  global oder auf den im Ordnerbaum gewählten Pfad eingeschränkt durchsuchen.

### Behoben
- Suche lieferte keine Treffer, wenn ein Ordner im Baum gewählt war (der
  Pfad-Filter verglich absolute mit indexrelativen Pfaden).

## [0.9.0]

### Hinzugefügt
- Drei-Panel-Oberfläche mit Datei-Browser, Volltext-Suche (SQLite FTS5) und
  HTML-Vorschau (WebView2).
- Markdown-Editor mit Schreibschutz, Tag-Leiste und atomarem Speichern.
- WikiLink-Graph, Tag-Cloud und Tag-Verwaltung.
- Konfigurierbare Indexierung mehrerer Wurzeln inklusive Glob-Ausschlüssen,
  `.mdignore`-Hierarchie und UI-seitiger Indexierungs-Pause.
- Einstellungs-Dialog mit Audit-Trail, Live-Log-Viewer und Health-Anzeige.

[Unveröffentlicht]: https://github.com/ReneSchustek/mdExplorer/compare/v0.9.0...HEAD
[0.9.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.9.0
