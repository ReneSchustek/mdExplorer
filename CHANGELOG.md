# Changelog

Alle nennenswerten Änderungen an diesem Projekt werden in dieser Datei
dokumentiert. Das Format orientiert sich an
[Keep a Changelog](https://keepachangelog.com/de/1.1.0/); die Versionierung
folgt [Semantic Versioning](https://semver.org/lang/de/).

## [Unveröffentlicht]

## [0.13.0] - 2026-08-01

Diese Fassung enthält keine Änderung am Funktionsumfang. Sie markiert den
Abschluss der Qualitätssicherung.

### Geändert
- Die Testabdeckung liegt jetzt bei 95 Prozent statt bei 86. Abgesichert sind
  vor allem die Wege, die im Betrieb still fehlschlagen: abgebrochene
  Ladevorgänge, eine kurzzeitig belegte Datenbank, gesperrte oder inzwischen
  gelöschte Dateien sowie eine nicht schreibbare Einstellungsdatei. In all
  diesen Fällen bleibt der zuletzt angezeigte Stand stehen, statt zu leeren.
- Der periodische Abgleich des Indexers und das Wiederholen beim Lesen sind
  jetzt durch Tests abgesichert. Beide sorgen dafür, dass eine Datei auch dann
  im Bestand landet, wenn die Dateisystem-Überwachung ihr Entstehen nicht
  gemeldet hat — etwa bei einem zwischenzeitlich getrennten Netzlaufwerk.

## [0.12.1] - 2026-08-01

### Behoben
- Falsch geschriebene Umlaute in Anzeigetexten korrigiert, darunter der Hinweis
  „Tag hinzufügen" im Dokument-Bereich und die Meldung beim Zusammenführen von
  Tags.

## [0.12.0] - 2026-07-31

### Hinzugefügt
- Updates lassen sich direkt aus der Anwendung installieren: `Einstellungen →
  Verhalten` prüft auf Anfrage, lädt das Installationspaket herunter, gleicht es
  gegen die veröffentlichte Prüfsumme ab und startet es. Stimmt die Prüfsumme
  nicht, wird die Datei verworfen statt ausgeführt.
- Programmsymbol: Die Anwendung erscheint in der Taskleiste jetzt mit eigenem
  Logo statt mit dem Standardsymbol.

### Geändert
- Der Startbildschirm hat einen hellen Hintergrund; das Logo fügt sich damit ein,
  statt als Kachel aufzusitzen.

## [0.11.0] - 2026-07-31

### Hinzugefügt
- Über-Dialog: freiwilliger Spenden-Eintrag. Wer möchte, kann die Weiterarbeit
  am Projekt mit einem Kaffee unterstützen; der Button öffnet die Spendenseite
  im Standardbrowser.

### Sicherheit
- Die mitgelieferte SQLite-Bibliothek läuft jetzt in einer Fassung ohne die
  bekannte Schwachstelle GHSA-2m69-gcr7-jv3q.

## [0.10.0] - 2026-07-22

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

### Geändert
- Umfangreiche Qualitäts- und Härtungsarbeiten: strengere Analyzer-Regeln
  wiederhergestellt, Deep-Quality-Durchlauf und frischer Security-Review,
  Testabdeckung auf ≥ 80 % angehoben sowie Datenschicht-Optimierung
  (Batch-Ladevorgänge statt N+1-Abfragen, case-insensitiver Pfad-Abgleich).
  Projektstruktur nach `src/` und `tests/` reorganisiert.

## [0.9.0] - 2026-06-26

### Hinzugefügt
- Drei-Panel-Oberfläche mit Datei-Browser, Volltext-Suche (SQLite FTS5) und
  HTML-Vorschau (WebView2).
- Markdown-Editor mit Schreibschutz, Tag-Leiste und atomarem Speichern.
- WikiLink-Graph, Tag-Cloud und Tag-Verwaltung.
- Konfigurierbare Indexierung mehrerer Wurzeln inklusive Glob-Ausschlüssen,
  `.mdignore`-Hierarchie und UI-seitiger Indexierungs-Pause.
- Einstellungs-Dialog mit Audit-Trail, Live-Log-Viewer und Health-Anzeige.

[Unveröffentlicht]: https://github.com/ReneSchustek/mdExplorer/compare/v0.13.0...HEAD
[0.13.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.13.0
[0.12.1]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.12.1
[0.12.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.12.0
[0.11.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.11.0
[0.10.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.10.0
[0.9.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.9.0
