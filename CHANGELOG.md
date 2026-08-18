# Changelog

Alle nennenswerten Änderungen an diesem Projekt werden in dieser Datei
dokumentiert. Das Format orientiert sich an
[Keep a Changelog](https://keepachangelog.com/de/1.1.0/); die Versionierung
folgt [Semantic Versioning](https://semver.org/lang/de/).

## [Unveröffentlicht]

## [0.15.3] - 2026-08-18

### Behoben
- **Das Handbuch versprach „kein Aufruf externer Server".** Das war falsch: Die
  Prüfung auf eine neue Fassung fragt beim Start die Veröffentlichungsliste des
  Projekts auf GitHub ab, und sie ist ab Werk eingeschaltet. Abschnitt 1 benennt
  jetzt beide Wege nach draußen — diese Prüfung und die Bilder von fremden
  Servern in der Vorschau — samt Drossel, Schalter und Werkseinstellung.

### Geändert
- Die Einstellung „Bilder aus dem Netz in der Vorschau laden" aus Fassung 0.15.1
  ist in der Dokumentation angekommen: im Handbuch beim Abschnitt zur Vorschau,
  in README und `docs/SETTINGS.md` in der Aufzählung des Verhalten-Reiters.
  Beschrieben war sie bisher nur in der Feldtabelle des Schemas.
- Unter der Haube aufgeräumt: Kommentare im Quelltext sagen das Warum statt die
  Vorgeschichte, und ein Test räumt seine Zwischendatei auch dann weg, wenn ein
  anderer Zugriff sie einen Augenblick festhält.

## [0.15.2] - 2026-08-17

### Behoben
- **Verweisgraph und Hilfe blieben in der installierten Fassung leer** und meldeten
  „Zugriff verweigert". Beide Fenster legten ihre Browser-Daten neben der
  Programmdatei ab — nach einer Installation also in einem Ordner, in den kein
  Benutzer schreiben darf. Sie benutzen jetzt dasselbe Verzeichnis wie die
  Vorschau, unterhalb der übrigen Anwendungsdaten. Wer die Anwendung aus dem
  Entwicklungsordner startete, hat davon nie etwas gemerkt.

### Geändert
- Die Verknüpfung auf dem Schreibtisch ist im Setup vorausgewählt und weiterhin
  abwählbar. Bisher war das Häkchen voreingestellt leer — wer es übersah, hatte
  keine Verknüpfung, und weil das Setup die getroffene Auswahl merkt, blieb das
  auch nach jeder neuen Fassung so.

## [0.15.1] - 2026-08-16

Nicht einzeln veröffentlicht — das Etikett wurde damals nicht gesetzt, und ohne
Etikett baut die Pipeline nichts. Die Änderungen stecken vollständig in den
Artefakten von 0.15.2.

### Behoben
- **Bilder in Notizen waren in der Vorschau nie zu sehen.** Ein Bild mit
  relativem Pfad — also praktisch jedes — hatte nichts, worauf es sich beziehen
  konnte: Die Vorschau bekommt ihr HTML als Zeichenkette, und darin gibt es keinen
  Ordner. Jetzt zeigen relative Pfade auf den Ordner des angezeigten Dokuments.

### Hinzugefügt
- Einstellungen → Verhalten: „Bilder aus dem Netz in der Vorschau laden".
  **Ab Werk aus**, damit die Zusage stimmt, dass die Anwendung ohne
  Internetverbindung vollständig arbeitet — ein Bild von einem fremden Server
  verrät ihm, wann Sie welche Notiz geöffnet haben. Eingeschaltet erscheinen auch
  Abzeichen und Bilder aus dem Netz; geöffnet wird dabei ausschließlich die
  Bildquelle, Skripte bleiben in jedem Fall gesperrt.

## [0.15.0] - 2026-08-16

### Geändert
- `vendor` und die Kernverzeichnisse fremder Systeme lassen sich über die
  Ausschlussmuster aus der Indizierung nehmen — und was bereits im Bestand steht,
  verschwindet jetzt beim nächsten Abgleich mit. In einem gewachsenen Bestand
  sank die Zahl der Einträge damit von 29.889 auf 4.315; übrig bleibt, was
  wirklich auf der Platte liegt und einem selbst gehört.

### Behoben
- Über einem großen Bestand wurde die Anwendung unbenutzbar: Die Dateiliste baute
  **jede** Zeile auf, auch die zehntausend, die niemand sieht. Über 29.889 Dateien
  belegte das mehr als 9 GB Arbeitsspeicher, und die Indizierung kam kaum voran.
  Jetzt sind es rund 500 MB, und die Indizierung ist fünfzehnmal schneller.
- Das Entfernen nicht mehr vorhandener Dateien aus der Volltextsuche brauchte rund
  eine sechstel Sekunde **je Datei**. Für einen Bestand, aus dem 25.000 Einträge
  wegfallen, war das über eine Stunde; jetzt sind es Sekunden. Als Folge davon
  verschwindet eine gelöschte Datei nicht mehr im selben Augenblick aus der
  Trefferliste, sondern mit dem nächsten Abgleich — dieselbe Frist von wenigen
  Sekunden, die für neue Dateien ohnehin gilt.
- Eine Kennzeichnung, an der keine Datei mehr hängt, blieb für immer im Bestand
  stehen. Sie wird jetzt weggeräumt.

## [0.14.1] - 2026-08-16

### Behoben
- Ein Filter mit mehreren Wörtern in Anführungszeichen suchte etwas anderes als
  angegeben. `tag:"zwei Wörter"` wurde als Schlagwort `zwei` gelesen, dazu das
  Wort `Wörter` irgendwo im Text; `path:"mein Ordner"` suchte einen Pfad, der mit
  einem Anführungszeichen beginnt.
- Ein Verweis mit einem Ziel, aus dem sich kein Sprungziel bilden lässt — etwa
  `[[…]]` mit drei Punkten als Auslassungszeichen — ließ die **ganze Datei** aus
  dem Bestand fallen. Ein unbrauchbares Ziel bleibt jetzt einfach ein Text.
- Der Aufräumdurchgang der Indizierung entfernt verschwundene Dateien auch dann,
  wenn beim Schreiben eines Stapels etwas schiefging. Bisher hing er am Erfolg
  des gesamten Durchlaufs — Einträge zu längst gelöschten Dateien blieben stehen.
  Bei einem nur teilweise lesbaren Verzeichnisbaum wird weiterhin nichts
  entfernt: Was niemand gesehen hat, ist nicht dasselbe wie gelöscht.

### Geändert
- Der Bereich „Zusammenhänge" lädt beim Klick auf ein Dokument nur noch dessen
  Nachbarschaft statt des gesamten Bestands.
- `vendor` ist ab Werk von der Indizierung ausgenommen — der Ordner, in dem PHP,
  Ruby und Go fremde Pakete ablegen. In einem gewachsenen Bestand lagen dort
  3.906 Markdown-Dateien fremder Dokumentation, die jede Trefferliste überlagern.
- Die gemessene Zeilenabdeckung liegt wieder über der Marke: 95,2 % bei 86,7 %
  Zweigabdeckung. Nicht mitgemessen sind Fenster, Bedienflächen-Code hinter der
  Oberfläche und die Verdrahtung der Dienste — 39 Typen mit zusammen 1.760
  Codezeilen, rund ein Sechstel des Produktivcodes.

## [0.14.0] - 2026-08-16

### Hinzugefügt
- Alle Listen tragen dieselbe Gestaltung: ein Suchfeld, das sagt, was es
  durchsucht, Zeitraum-Schalter, Filter für Ordner und Kennzeichnungen als
  einzeln abnehmbare Merkzettel, sichtbare Gruppen und eine Sprungleiste von A
  bis Z. Leere Listen unterscheiden „nichts vorhanden" von „nichts gefunden" und
  bieten im zweiten Fall an, die Einschränkungen zurückzunehmen.
- Bereich „Zusammenhänge" unter der Vorschau: Er zeigt, wohin ein Dokument
  verweist und wer auf es verweist, dazu seinen Ordner und seine
  Kennzeichnungen. Jeder Eintrag ist ein Weg dorthin, keine bloße Angabe.
- Umbenennen, Verschieben und Löschen einer Datei aus diesem Bereich heraus.
  Vor dem Eingriff steht, wie viele Dokumente danach ins Leere zeigen — vorher,
  nicht hinterher.
- Dunkles Erscheinungsbild für die gesamte Oberfläche: Register, Menü,
  Schaltflächen, Titelleiste, Auswahl, Vorschau und Schlagwortverwaltung.

### Behoben
- Die Trefferstellen der Suche waren im dunklen Erscheinungsbild nicht lesbar —
  helle Schrift auf hellem Grund. Sie folgen jetzt der Farbbelegung und wechseln
  mit ihr.
- Die Schlagwortverwaltung ließ sich gar nicht öffnen: Ein Ressourcen-Block
  stand hinter seiner Verwendung.
- Das Menü klappte über den rechten Fensterrand hinaus, wenn Windows „Menüs
  rechtsbündig ausrichten" gesetzt hat.
- Der Verweis-Graph zeichnete unabhängig vom gewählten Erscheinungsbild immer
  dunkel und blieb nach einer Größenänderung des Fensters leer.
- Zeitangaben in der Statusleiste und in der Dateiliste standen in koordinierter
  Weltzeit statt in der Zeit des Rechners.
- Das projektweite Umbenennen einer Kennzeichnung ließ Vorkommen stehen, die die
  Indizierung sehr wohl erfasst hatte. Beide benutzen jetzt dieselbe Regel.
- Farbwerte wie `#F59E0B` landeten als Kennzeichnung im Bestand. Sie werden
  nicht mehr aufgenommen.

### Geändert
- Lange Abfragen brechen beim Beenden der Anwendung ab, statt das Schließen
  aufzuhalten.

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

[Unveröffentlicht]: https://github.com/ReneSchustek/mdExplorer/compare/v0.15.3...HEAD
[0.15.3]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.15.3
[0.15.2]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.15.2
[0.15.1]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.15.2
[0.15.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.15.0
[0.14.1]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.14.1
[0.14.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.14.0
[0.13.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.13.0
[0.12.1]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.12.1
[0.12.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.12.0
[0.11.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.11.0
[0.10.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.10.0
[0.9.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.9.0
