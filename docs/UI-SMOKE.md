# UI-Smoke-Plan (WPF)

> Pflicht-Checkliste vor jedem Release. Wird in einem laufenden Build der App durchgespielt, nicht
> gegen Unit-Tests. Tests prüfen ViewModel-Logik — dieser Plan prüft Bindings, Rendering,
> Eingabe-Routing und WebView2.

## Warum manuell (Stand 2026-06-21)

Erwogen waren zwei Varianten:

- **Option A — FlaUI-basierte Smoke-Suite**: Neue NuGet-Abhängigkeiten (`FlaUI.Core`, `FlaUI.UIA3`)
  benötigen laut `general.md` (Library-Gate) explizite User-Freigabe. WebView2-Wartepfade
  (`CoreWebView2`-Initialisierung) sind unter UIA3 erfahrungsgemäß flaky. Aufwand/Nutzen für ein
  Single-Dev-Tool mit Hardening-Priorität ist negativ.
- **Option B — Manueller Smoke-Plan**: Sofort einsatzbereit, kein neuer Abhängigkeits-Footprint,
  deckt die acht Pflicht-Pfade reproduzierbar ab. Risiko des Verfalls wird durch einen
  Pflicht-Lauf-Bericht pro Release abgefangen (siehe Abschnitt _Lauf-Berichte_).

Entscheidung: **Option B**. Eine Migration auf FlaUI bleibt möglich, sobald die Pflichtpfade sich
stabilisiert haben und der WebView2-Wait-Pattern lösbar ist.

## Vorbereitung

1. Letzte Änderungen committen oder stashen.
2. Build: `dotnet build -c Release MdExplorer.slnx`.
3. App starten: `dotnet run --project MdExplorer.App -c Release` _oder_ aus dem Publish-Output.
4. Test-Workspace mit mindestens fünf Markdown-Dateien und drei `[[WikiLinks]]` bereithalten.
5. Lauf-Bericht-Vorlage in `reports/ui-smoke-<YYYY-MM-DD>.md` anlegen
   (Vorlage am Ende dieses Dokuments).

## Pflicht-Checkliste

| # | Szenario | Schritte | Erwartung | Acceptance |
|---|---|---|---|---|
| 1 | **Cold-Start** | App-Prozess starten | Splash erscheint kurz, danach Hauptfenster mit `FolderTreePanel`. Mindestens ein Workspace-Root sichtbar. Status-Leiste zeigt "Indizierte Dateien" und "Letzter Indexer-Lauf". | PASS / FAIL / N.A. |
| 2 | **Suche** | Tab "Suche" anklicken, Wort eingeben, das in mind. einer Datei vorkommt | `ListBox` zeigt Treffer mit Titel, Pfad, Highlight-Snippet. Klick navigiert zur Datei; `DocumentPanel` zeigt sie an. | PASS / FAIL / N.A. |
| 3 | **Ctrl+F im Dokument** | Dokument im Read-Modus öffnen, `Strg+F` drücken | `FindBar` im `DocumentPanel` wird sichtbar, `FindInput` hat Fokus. Wort eingeben → Treffer wird im Preview markiert und gescrollt. `Esc` schließt die FindBar. | PASS / FAIL / N.A. |
| 4 | **Tab-Layout-Persistenz** | Tab "Alle Dateien" auswählen, App über Schließen-Button beenden, neu starten | Beim Neustart ist Tab "Alle Dateien" aktiv (gleicher `LeftTabIndex`). Spaltenbreiten `FolderColumn` / `PreviewColumn` sind wiederhergestellt. | PASS / FAIL / N.A. |
| 5 | **Tag-Cloud-Toggle** | `Strg+T` drücken (zweimal: an + aus). App schließen+neustarten | Tag-Cloud-Panel ein-/ausblenden funktioniert sofort, ohne Layout-Sprung. Letzter Zustand bleibt nach Neustart erhalten (Menü "Ansicht → Tag-Cloud" zeigt korrekte Checkbox). | PASS / FAIL / N.A. |
| 6 | **Graph-Fenster** | Menü "Ansicht → Graph…" öffnen. Pfad-Prefix eintragen (z.B. `notes/`). "Neu laden" drücken. App schließen+neustarten, Graph wieder öffnen | Status-Leiste zeigt `Knoten: X von Y` und `Kanten: A von B`. Nach Prefix-Eingabe + Refresh reduziert sich `X` (gefiltert) gegenüber `Y` (Original). Nach Neustart ist `PathPrefix`-Wert noch im Eingabefeld vorbelegt. | PASS / FAIL / N.A. |
| 7 | **WebView2-CSP** | Graph-Fenster offen halten. Devtools per Rechtsklick → "Inspect" öffnen | `Console`-Tab zeigt keine CSP-Verstöße ("Refused to execute inline script …"). Im `Network` / `Sources` ist `'unsafe-inline'` nicht gesetzt; statt dessen Nonce auf `<script>`-Tag. | PASS / FAIL / N.A. |
| 8 | **Indexer-Status** | Direkt nach Cold-Start die Status-Leiste beobachten (1–2 min) | "Indizierte Dateien" steigt sichtbar an, während der Indexer läuft. "Letzter Indexer-Lauf" wechselt von `—` auf einen Zeitstempel `YYYY-MM-DD HH:mm UTC`. Health-Indikator (Ampel links) bleibt grün oder wechselt nur zu gelb/rot, wenn Logs einen Grund nennen. | PASS / FAIL / N.A. |

## Lauf-Berichte

- Lauf-Berichte werden unter `reports/ui-smoke-<YYYY-MM-DD>.md` abgelegt.
- Bei jedem **Release-Tag** ist mindestens ein grüner Lauf-Bericht Pflicht.
- Roter Lauf (FAIL) blockiert das Release. Befund wird als BRIEF angelegt, bevor erneut gespielt wird.
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
| 3 | Ctrl+F | PASS / FAIL / N.A. | … |
| 4 | Tab-Layout-Persistenz | PASS / FAIL / N.A. | … |
| 5 | Tag-Cloud-Toggle | PASS / FAIL / N.A. | … |
| 6 | Graph-Fenster | PASS / FAIL / N.A. | … |
| 7 | WebView2-CSP | PASS / FAIL / N.A. | … |
| 8 | Indexer-Status | PASS / FAIL / N.A. | … |

## Auffälligkeiten
- …
```
