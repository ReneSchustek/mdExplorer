# UI-Smoke-Plan (WPF)

> Pflicht-Checkliste vor jedem Release. Wird in einem laufenden Build der App durchgespielt, nicht
> gegen Unit-Tests. Tests prüfen ViewModel-Logik, dieser Plan prüft Bindings, Rendering,
> Eingabe-Routing und WebView2.

## Warum manuell (Stand 2026-06-21)

Erwogen waren zwei Varianten:

- **Option A (FlaUI-basierte Smoke-Suite)**: Neue NuGet-Abhängigkeiten (`FlaUI.Core`, `FlaUI.UIA3`)
  vergrößern den Abhängigkeits-Footprint und sind deshalb begründungspflichtig. WebView2-Wartepfade
  (`CoreWebView2`-Initialisierung) sind unter UIA3 erfahrungsgemäß flaky. Aufwand/Nutzen für ein
  Single-Dev-Tool mit Hardening-Priorität ist negativ.
- **Option B (Manueller Smoke-Plan)**: Sofort einsatzbereit, kein neuer Abhängigkeits-Footprint,
  deckt die Pflicht-Pfade reproduzierbar ab. Risiko des Verfalls wird durch einen
  Pflicht-Lauf-Bericht pro Release abgefangen (siehe Abschnitt _Lauf-Berichte_).

Entscheidung: **Option B**. Eine Migration auf FlaUI bleibt möglich, sobald die Pflichtpfade sich
stabilisiert haben und der WebView2-Wait-Pattern lösbar ist.

## Vorbereitung

1. Letzte Änderungen committen oder stashen.
2. Build: `dotnet build -c Release MdExplorer.slnx`.
3. App starten: `dotnet run --project src/MdExplorer.App -c Release` _oder_ aus dem Publish-Output.
4. Test-Workspace mit mindestens fünf Markdown-Dateien und drei `[[WikiLinks]]` bereithalten.
5. Lauf-Bericht nach der Vorlage am Ende dieses Dokuments anlegen.

## Pflicht-Checkliste

| # | Szenario | Schritte | Erwartung | Acceptance |
|---|---|---|---|---|
| 1 | **Cold-Start** | App-Prozess starten | Splash erscheint kurz, danach Hauptfenster mit `FolderTreePanel`. Mindestens ein Workspace-Root sichtbar. Status-Leiste zeigt "Indizierte Dateien" und "Noch keine Indexer-Meldung" bzw. "Letzte Indexer-Meldung". | PASS / FAIL / N.A. |
| 2 | **Suche** | Register „Suche" anklicken, Wort eingeben, das in mind. einer Datei vorkommt | `ListBox` zeigt Treffer mit Titel, Pfad, Highlight-Snippet. Klick navigiert zur Datei; `DocumentPanel` zeigt sie an. | PASS / FAIL / N.A. |
| 3 | **Strg+F im Dokument** | Dokument öffnen, mit `Strg+E` in den Bearbeiten-Modus wechseln, `Strg+F` drücken | `FindBar` im `DocumentPanel` wird sichtbar, `FindInput` hat Fokus. Wort eingeben → Treffer wird im Editor markiert und gescrollt, `F3` springt weiter. `Esc` schließt die FindBar. Im Lese-Modus springt `Strg+F` stattdessen ins Suchfeld des Registers „Suche". | PASS / FAIL / N.A. |
| 4 | **Tab-Layout-Persistenz** | Register „Alle Dateien" auswählen, App über Schließen-Button beenden, neu starten | Beim Neustart ist „Alle Dateien" aktiv (gleicher `LeftTabIndex`). Spaltenbreiten `FolderColumn` / `PreviewColumn` sind wiederhergestellt. | PASS / FAIL / N.A. |
| 5 | **Tag-Cloud-Toggle** | `Strg+T` drücken (zweimal: an + aus), danach den Tag-Cloud-Knopf in der Kopfleiste und das ✕ im Kopf der Tag-Cloud. App schließen+neustarten | Tag-Cloud ein-/ausblenden funktioniert sofort, ohne Layout-Sprung; der Knopf in der Kopfleiste zeigt den Zustand. Letzter Zustand bleibt nach Neustart erhalten. | PASS / FAIL / N.A. |
| 6 | **Graph-Fenster** | Graph-Knopf in der Kopfleiste drücken. Pfad-Prefix eintragen (z.B. `notes/`). "Neu laden" drücken. App schließen+neustarten, Graph wieder öffnen | Status-Leiste zeigt `Knoten: X von Y` und `Kanten: A von B`. Nach Prefix-Eingabe + Refresh reduziert sich `X` (gefiltert) gegenüber `Y` (Original). Nach Neustart ist `PathPrefix`-Wert noch im Eingabefeld vorbelegt. | PASS / FAIL / N.A. |
| 7 | **WebView2-CSP** | Graph-Fenster offen halten. Devtools per Rechtsklick → "Inspect" öffnen | `Console`-Tab zeigt keine CSP-Verstöße ("Refused to execute inline script …"). Im `Network` / `Sources` ist `'unsafe-inline'` nicht gesetzt; statt dessen Nonce auf `<script>`-Tag. | PASS / FAIL / N.A. |
| 8 | **Indexer-Status** | Direkt nach Cold-Start die Status-Leiste beobachten (1–2 min) | "Indizierte Dateien" steigt sichtbar an, während der Indexer läuft. "Noch keine Indexer-Meldung" wechselt auf "Letzte Indexer-Meldung: TT.MM.JJJJ HH:mm" in Ortszeit. Health-Indikator (Ampel links) bleibt grün oder wechselt nur zu gelb/rot, wenn Logs einen Grund nennen. | PASS / FAIL / N.A. |
| 9 | **Doppelklick** | MdExplorer schließen. Im Explorer eine `.md`-Datei außerhalb der indizierten Ordner per „Öffnen mit → MdExplorer“ öffnen | MdExplorer startet und zeigt die Datei in Vorschau und Editor; die Bezüge bleiben leer. Eine `.txt`-Datei über die Befehlszeile (`MdExplorer.exe notiz.txt`) ergibt die Meldung „Datei kann nicht geöffnet werden“. | PASS / FAIL / N.A. |
| 10 | **Zweite Instanz** | Bei laufendem, minimiertem MdExplorer eine weitere `.md`-Datei öffnen | Die Datei erscheint im vorhandenen Fenster, das Fenster kommt nach vorn. Im Task-Manager läuft genau ein `MdExplorer.exe`. | PASS / FAIL / N.A. |
| 11 | **Doppelklick bei ungespeicherten Änderungen** | Im Editor Text ändern, nicht speichern. Eine andere `.md`-Datei per Doppelklick öffnen, „Abbrechen“ wählen; noch einmal, „Nein“ wählen | Genau eine Rückfrage erscheint. „Abbrechen“ lässt die geänderte Datei offen, „Nein“ öffnet die neue. Zwei schnelle Doppelklicks während der Rückfrage erzeugen keine zweite Rückfrage. | PASS / FAIL / N.A. |
| 12 | **Schmales Fenster** | Fenster auf etwa 900 Pixel Breite ziehen, Tag-Cloud über den Knopf einschalten, Trenner links mit der Maus und mit den Pfeiltasten bewegen. App schließen+neustarten | Unter 960 Pixeln blendet sich die Tag-Cloud aus; auf Knopfdruck erscheint sie samt ✕, nichts ist am rechten Rand abgeschnitten. Die mit dem Trenner gesetzte Breite der linken Spalte bleibt nach dem Neustart erhalten. | PASS / FAIL / N.A. |
| 13 | **Verlauf** | Zwei Dateien öffnen, eine davon außerhalb des Index. App schließen+neustarten, `Strg+R` drücken, ältere Datei wählen. Eine Datei im Explorer umbenennen, Verlauf erneut aufklappen und sie anklicken. Rechtsklick auf das Taskleistensymbol. „Verlauf leeren“ | Beide Dateien stehen nach dem Neustart im Verlauf, jüngste oben; die gewählte öffnet. Die umbenannte steht als „nicht gefunden“ da und verschwindet beim Klick. Die Sprungliste zeigt dieselben Dateien; nach „Verlauf leeren“ sind Verlauf und Sprungliste leer. | PASS / FAIL / N.A. |

## Lauf-Berichte

- Lauf-Berichte gehören zu den Release-Unterlagen und werden nicht ins Repository eingecheckt;
  das Repository enthält nur Code, Tests und Dokumentation.
- Bei jedem **Release-Tag** ist mindestens ein grüner Lauf-Bericht Pflicht, abgelegt vor dem Setzen
  des Tags.
- Roter Lauf (FAIL) blockiert das Release. Der Befund wird festgehalten und behoben, bevor erneut gespielt wird.
- Bei `N.A.` (Szenario nicht anwendbar, z.B. weil Feature deaktiviert) ist eine kurze Begründung im
  Bericht zu nennen.

## Vorlage (Copy & Paste)

```markdown
# UI-Smoke-Lauf YYYY-MM-DD

- **Tester:** <Name>
- **Build/Commit:** <git rev-parse --short HEAD>
- **App-Version:** <VERSION-Datei>
- **OS:** Windows 11 Pro <Build>

| # | Szenario | Ergebnis | Notiz |
|---|---|---|---|
| 1 | Cold-Start | PASS / FAIL / N.A. | … |
| 2 | Suche | PASS / FAIL / N.A. | … |
| 3 | Strg+F im Dokument | PASS / FAIL / N.A. | … |
| 4 | Tab-Layout-Persistenz | PASS / FAIL / N.A. | … |
| 5 | Tag-Cloud-Toggle | PASS / FAIL / N.A. | … |
| 6 | Graph-Fenster | PASS / FAIL / N.A. | … |
| 7 | WebView2-CSP | PASS / FAIL / N.A. | … |
| 8 | Indexer-Status | PASS / FAIL / N.A. | … |
| 9 | Doppelklick | PASS / FAIL / N.A. | … |
| 10 | Zweite Instanz | PASS / FAIL / N.A. | … |
| 11 | Doppelklick bei ungespeicherten Änderungen | PASS / FAIL / N.A. | … |
| 12 | Schmales Fenster | PASS / FAIL / N.A. | … |
| 13 | Verlauf | PASS / FAIL / N.A. | … |

## Auffälligkeiten
- …
```
