# MdExplorer — Handbuch

Dieses Handbuch beschreibt MdExplorer von der ersten Begegnung bis zum
Alltagsgebrauch. Es ist als zusammenhängender Text gedacht und nicht
als Nachschlagewerk: wer es einmal vorne nach hinten durchliest,
versteht hinterher, was die drei Spalten der Oberfläche bedeuten, wie
der Index entsteht, wie Suche und Vorschau zusammenspielen, und wo die
Anwendung ihre Daten ablegt.

## Inhalt {#inhalt}

1. [Was MdExplorer ist — und was nicht](#einfuehrung)
2. [Installation und erster Start](#installation)
3. [Die Oberfläche](#layout)
4. [Indexierung, Ausschlüsse und `.mdignore`](#indexierung)
5. [Suche und Vorschau](#suche)
6. [Tag-Cloud und Tag-Verwaltung](#tagcloud)
7. [Die Graph-Ansicht](#graph)
8. [Häufig gestellte Fragen](#faq)

---

## 1. Was MdExplorer ist — und was nicht {#einfuehrung}

MdExplorer ist ein Desktop-Werkzeug für Menschen, die mit
Markdown-Beständen arbeiten und in ihnen schnell etwas wiederfinden
möchten. Das Programm liest ein oder mehrere Verzeichnisse rekursiv
ein, legt einen lokalen Index an und stellt drei Werkzeuge zur
Verfügung: einen Datei-Browser, eine Volltext-Suche und eine HTML-
Vorschau. Wer Dutzende oder Hunderte Notizen pflegt, sei es ein
persönliches Zettelkasten-System, eine technische Wissensbasis oder
eine umfangreiche Projektdokumentation, bekommt mit MdExplorer einen
Überblick, den ein einfacher Datei-Explorer nicht liefert.

MdExplorer ist ausdrücklich **kein Markdown-Editor mit großem
Funktionsumfang**. Es gibt zwar einen einfachen Bearbeiten-Modus, der
für kurze Korrekturen und das Pflegen von Tags ausreicht, aber für
ausführliches Schreiben bleibt ein dedizierter Editor (etwa Typora,
Obsidian oder VS Code) die bessere Wahl. Genauso wenig ist MdExplorer
ein Cloud-Dienst: alle Daten (Index, Einstellungen, Logs) bleiben
auf dem lokalen Rechner. Es findet keine Synchronisierung statt und kein
Telemetrie-Versand; ohne Internetverbindung arbeitet die Anwendung vollständig.

Zwei Wege nach draußen gibt es, beide sichtbar und beide abschaltbar. Die
**Prüfung auf eine neue Fassung** fragt beim Start die Veröffentlichungsliste des
Projekts auf GitHub ab — ab Werk eingeschaltet, höchstens einmal in 24 Stunden,
abzuschalten unter `Einstellungen → Verhalten`. Und **Bilder von fremden Servern**
lädt die Vorschau nur, wenn Sie es dort ausdrücklich erlauben; ab Werk tut sie
es nicht.

Die Stärken liegen in drei Bereichen. Erstens beim **Auffinden** von
Inhalten in größeren Markdown-Sammlungen: Eine Volltext-Suche, die
nach einem Tippstopp im Bereich von Millisekunden Treffer liefert,
verändert den Umgang mit Notizen. Zweitens beim **Sichtbarmachen von
Strukturen**: WikiLinks der Form `[[Dateiname]]` werden geparst und in
einer Graph-Ansicht dargestellt, sodass sich Cluster und isolierte
Notizen sofort erkennen lassen. Drittens beim **Klassifizieren über
Tags**: sowohl Frontmatter-Felder als auch Inline-Hashtags fließen in
eine Tag-Cloud zusammen, die als zusätzlicher Sucheinstieg dient.

Wer mit Plain-Text arbeitet, Daten ungern aus der Hand gibt und
schnelle Wiederfindbarkeit braucht, findet in MdExplorer ein passendes
Werkzeug.

## 2. Installation und erster Start {#installation}

MdExplorer setzt **Windows 10 oder 11** und die **WebView2-Runtime**
voraus; ein installiertes .NET braucht die Anwendung nicht, sie bringt es
mit. Auf aktuellen Windows-Installationen ist
die WebView2-Runtime bereits Teil des Systems; sollte sie fehlen,
liefert Microsoft sie kostenlos auf
<https://developer.microsoft.com/microsoft-edge/webview2/>. Fehlt sie,
stehen Vorschau, Handbuch und Graph mit einem Hinweis darauf leer; der
Rest der Anwendung funktioniert. Verweise im Handbuch auf Seiten im
Netz öffnen im Standardbrowser. SQLite
wird über das NuGet-Paket `Microsoft.Data.Sqlite` bezogen und
benötigt keine separate Installation.

Installiert wird über das Setup von der Releases-Seite; alternativ läuft
das Archiv aus jedem beliebigen Ordner, ohne Installation. Wer aus dem
Quelltext baut, braucht zusätzlich das .NET 10 SDK:

```powershell
git clone <repository-url> MdExplorer
cd MdExplorer
dotnet run --project src/MdExplorer.App
```

### Markdown-Dateien per Doppelklick öffnen

Das Setup trägt MdExplorer bei Windows als Programm für `.md` und
`.markdown` ein. Im Explorer steht es danach im Kontextmenü unter
„Öffnen mit“. Zum Standardprogramm wird es mit „Öffnen mit → Andere App
auswählen“, dort MdExplorer markieren und „Immer“ wählen; diese Wahl
trifft Windows nur auf Anweisung des Nutzers, kein Setup darf sie
vorwegnehmen. Wer das Archiv ohne Setup nutzt, sucht in demselben Dialog
die `MdExplorer.exe` im entpackten Ordner aus.

Ein Doppelklick öffnet die Datei dann im Dokumentbereich, auch wenn sie
in keinem indizierten Ordner liegt. Sie erscheint in Vorschau und Editor
wie jede andere; nur die Bezüge bleiben leer, bis der Ordner indiziert
ist.

- **Programm läuft schon:** Die Datei öffnet im vorhandenen Fenster, und
  das Fenster kommt nach vorn, auch aus der Taskleiste heraus. Eine
  zweite Instanz entsteht nicht; sie teilte sich sonst Datenbank und
  Indexer mit der ersten.
- **Ungespeicherte Änderungen:** Steht im Editor noch Ungespeichertes,
  kommt dieselbe Frage wie beim Wechsel der Datei. „Abbrechen“ lässt
  das bisherige Dokument offen.
- **Datei fehlt oder ist keine Markdown-Datei:** MdExplorer meldet das
  mit dem Pfad und öffnet nichts.
- **Programm antwortet nicht:** Reagiert das laufende Programm nicht
  binnen fünf Sekunden, meldet das die neu gestartete Instanz und
  beendet sich wieder. Endet das laufende Programm gerade, übernimmt
  die neue Instanz und öffnet die Datei selbst.

Werden mehrere Dateien zugleich geöffnet, zeigt MdExplorer die zuletzt
übergebene. Beim Deinstallieren entfernt das Setup die Zuordnung
wieder; andere Programme, die Markdown öffnen, behalten ihre.

Beim ersten Start öffnet MdExplorer das Hauptfenster, hat aber noch keine
Inhalte: weil keine Indexierungs-Wurzel konfiguriert ist, bleiben
Ordner-Baum, Dateiliste und Vorschau leer.
Der erste sinnvolle Schritt ist daher der Aufruf der Einstellungen
über das Zahnrad in der Kopfleiste oder das Tastenkürzel `Strg+,`. Im
Reiter „Indexierung" lässt sich ein oder mehrere Ordner-Pfade
eintragen, in denen sich Markdown-Dateien befinden. Sobald die
Einstellungen mit „Anwenden" bestätigt sind, beginnt der Hintergrund-
Indexer mit dem Scan; je nach Bestandsgröße sind die ersten Treffer
nach wenigen Sekunden verfügbar.

Alle Benutzerdaten liegen unterhalb von `%LOCALAPPDATA%\MdExplorer\`.
Dort finden sich die Datei `settings.json` mit den persistierten
Einstellungen, die Datei `ui-layout.json` mit den Spaltenbreiten des
Hauptfensters und weiteren Fensterzuständen, die SQLite-Datenbank `app.db` mit dem FTS5-Suchindex
und ein Unterverzeichnis `logs\` mit tageweise rotierten Logdateien.
Wer die Anwendung deinstalliert, kann diesen Ordner gefahrlos löschen;
umgekehrt lässt sich der gesamte Zustand durch Kopieren des Ordners
auf einen anderen Rechner mitnehmen.

## 3. Die Oberfläche {#layout}

Oben im Hauptfenster steht eine schlanke Kopfleiste: links der Name,
rechts die Werkzeuge als Symbole — Tag-Cloud ein und aus, Tag-Verwaltung,
Graph, Protokoll, Einstellungen, Handbuch und Info. Wer mit der Maus auf
einem Symbol verweilt, liest, was es tut und welches Tastenkürzel dazu
gehört.

Darunter gliedert sich das Fenster in Spalten, die mit einem Splitter
frei in der Breite anpassbar sind: links die Register zum Finden, rechts
das Dokument. Die Spaltenbreiten persistieren beim Schließen der
Anwendung in `ui-layout.json`, sodass das gewohnte Layout beim nächsten
Start wiederhergestellt wird. Ganz rechts lässt sich zusätzlich eine
Tag-Cloud-Spalte einblenden.

Die **linke Spalte** zeigt drei Register als Segmentleiste: „Ordner",
„Alle Dateien" und „Suche"; das gewählte hebt sich hell und fett ab. Das
erste Register „Ordner"
stellt die konfigurierten Indexierungs-Wurzeln als Baum dar; jeder
Wurzelpfad bildet einen eigenen Stamm. Ein Klick auf einen Ordner
wählt diesen als Bezugspfad; ob die Trefferliste daraufhin auf
Dateien innerhalb dieses Pfades eingeschränkt wird, steuert das
Kontrollkästchen „Nur aktueller Ordner" unter dem Suchfeld (siehe
Abschnitt 5). Per Rechtsklick lässt sich ein Ordner über das Kontextmenü
„Indexierung pausieren" temporär aus dem Index nehmen; der Ordner
und alle Unterordner werden grau dargestellt und beim nächsten
Indexer-Lauf übersprungen. Das zweite Register „Alle Dateien" zeigt
demgegenüber eine flache Liste aller indizierten Dateien ohne Baum-
Struktur; das hilft, wenn man weiß, wonach man sucht, sich aber den
genauen Ablageort nicht merken möchte.

Diese Liste bringt drei Hilfen mit. Das **Suchfeld** darüber filtert
beim Tippen über Titel, Pfad und Tags; `Escape` leert es.
Darunter stehen die **Zeitraum-Umschalter** „Alle", „Heute", „7 Tage"
und „30 Tage", die auf die letzte Änderung sehen. Ein Klick auf den
Pfad eines Eintrags schränkt auf dessen **Ordner** ein, ein Klick auf
einen Tag auf diesen **Tag**; mehrere davon wirken
zusammen. Jeder gesetzte Filter erscheint als Merkzettel über der
Liste und lässt sich dort einzeln wieder abnehmen — ein Filter, der
wirkt, aber nicht zu sehen ist, sieht aus wie ein leerer Bestand.

Ist die Liste nach Titel oder Pfad sortiert und lang genug, erscheint
zusätzlich die **Sprungleiste** `A B C … Z #`. Sie springt, sie
filtert nicht: Ein Klick auf „M" rollt zur Gruppe M, der übrige
Bestand bleibt sichtbar. Buchstaben ohne Einträge bleiben stehen und
sind abgeblendet. Die Liste ist nach denselben Buchstaben sichtbar
gruppiert; nach Datum sortiert entfallen Gruppen und Leiste, weil ein
Sprung auf „M" dann an beliebiger Stelle landen würde.

Bleibt die Liste leer, sagt sie, warum: „Noch nichts indiziert" bei
leerem Bestand, „Nichts gefunden" bei zu enger Suche (dort mit einer
Schaltfläche, die Suche und Filter zurücknimmt) und „Die Liste konnte
nicht geladen werden", wenn der Abruf gescheitert ist.

Das dritte Register „Suche" trägt das Suchfeld und darunter die
Trefferliste. Sobald im Suchfeld eine Anfrage steht, liefert die
FTS5-Suche Snippets mit hervorgehobenen Treffern, begrenzt auf die
eingestellte Zahl an Treffern je Seite. Ein Klick auf einen Eintrag
öffnet die Datei in der rechten Spalte. Einzelheiten stehen in
Kapitel 5.

Die **rechte Spalte** ist das Dokument-Panel. Im Standard-Modus
„Lesen" rendert eine eingebettete WebView2-Instanz die Markdown-Datei
als HTML in der im Settings-Dialog gewählten Schriftgröße. Über
`Strg+E` oder den Toolbar-Button „Bearbeiten" lässt sich der Modus
auf „Bearbeiten" umschalten; an dieser Stelle erscheint eine
Monospace-TextBox mit dem Rohtext. Tags lassen sich über eine Leiste
am oberen Rand der Datei manuell hinzufügen, entfernen oder
umbenennen; `Strg+S` speichert die Änderungen atomar zurück in die
Datei. Wer eine Datei extern verändert während sie im Bearbeiten-
Modus offen ist, bekommt vor dem Speichern eine Warnung und kann den
Konflikt manuell auflösen.

Unter dem Dokument steht der Bereich **Zusammenhänge**. Er zeigt, worauf
das Dokument verweist, welche Dokumente auf es verweisen, in welchem
Ordner es liegt und welche Tags es trägt; jeder Eintrag führt
weiter. Dort wird die Datei auch **umbenannt, verschoben oder gelöscht**,
im Zusammenhang, nicht in einer getrennten Verwaltungsseite. Vor dem
Löschen und vor einer Umbenennung, die Verweise bricht, nennt die
Rückfrage vorher, wie viele Dokumente danach ins Leere zeigen. Beim
Verschieben entfällt die Rückfrage, weil ein WikiLink auf den Dateinamen
zeigt und nicht auf den Pfad.

Die **optionale Spalte ganz rechts** ist die Tag-Cloud. Der Knopf in der
Kopfleiste, das ✕ in ihrem Kopf und `Strg+T` blenden sie ein und aus.
In einem Fenster unter 960 Pixeln Breite blendet sie sich selbst aus,
damit die Vorschau Platz behält, und kommt beim Verbreitern zurück; die
gespeicherte Wahl bleibt dabei unberührt. Wer sie im schmalen Fenster
über den Knopf einschaltet, behält sie. Wird es eng, gibt zuerst die
linke Spalte nach, dann die Tag-Cloud bis zu ihrer Mindestbreite. Sie zeigt
die häufigsten Tags des aktuellen Bestands; Details dazu folgen in
Kapitel 6.

### Helles und dunkles Erscheinungsbild

In den Einstellungen unter „Darstellung" steht die Wahl zwischen
**System**, **Hell** und **Dunkel**. „System" folgt der Einstellung von
Windows; die beiden anderen setzen sich darüber hinweg. Der Wechsel wirkt
sofort und ohne Neustart.

Die Wahl gilt für die **gesamte** Anwendung, auch für die Dokument-Vorschau,
den Verweis-Graphen und die Titelleisten der Fenster. Wer „Dunkel" wählt,
während Windows hell steht, bekommt deshalb keine weiße Fläche mitten in einer
dunklen Anwendung.

Spaltenbreiten, sichtbare Tag-Cloud und das zuletzt gewählte Register bleiben
vom Erscheinungsbild unberührt; sie stehen in `ui-layout.json` und gelten
unabhängig davon.

## 4. Indexierung, Ausschlüsse und `.mdignore` {#indexierung}

Der Indexer ist ein Hintergrunddienst, der unter drei Bedingungen
aktiv wird: beim ersten Start mit konfigurierten Wurzeln, nach
Änderungen an der Einstellungs-Datei und während des Betriebs durch
einen `FileSystemWatcher`, der Schreibzugriffe innerhalb der
Wurzelpfade beobachtet. Zusätzlich läuft im konfigurierbaren Intervall
(standardmäßig alle fünf Minuten) ein Soll/Ist-Abgleich, der
Inkonsistenzen (etwa beim Aufwachen aus dem Standby) erkennt und
auflöst.

Beim Indexer-Lauf liest der Scanner die Verzeichnisse rekursiv, prüft
für jede `.md`-Datei den **Inhalts-Hash** (SHA-256 der Bytes) und
vergleicht ihn mit dem zuletzt gespeicherten Hash in der Datenbank.
Nur bei Abweichung wird die Datei neu geparst und das FTS5-Schreib-
Statement abgesetzt. Diese Hash-Pipeline sorgt dafür, dass ein
Touch ohne Inhaltsänderung den Index nicht aufbläht und unveränderte
Dateien beim Re-Scan in Bruchteilen einer Sekunde abgearbeitet sind.

Welche Dateien überhaupt durchsucht werden, entscheidet ein dreistufiges
Filter-System. Zuerst gelten die **Glob-Ausschluss-Muster** aus der
`settings.json`. Standardmäßig sind die typischen Werkzeug-Verzeichnisse
`.git`, `node_modules`, `vendor`, `bin`, `obj` und `.vs` ausgeschlossen; weitere
Muster lassen sich im Einstellungs-Dialog ergänzen. Danach kommt die
**`.mdignore`-Hierarchie**: in jedem Verzeichnis unterhalb einer Wurzel
darf eine Datei `.mdignore` liegen, die zusätzliche Ausschluss-Muster
mit `.gitignore`-ähnlicher Syntax beisteuert. Wie bei `.gitignore` gelten
ihre Muster relativ zu dem Ordner, in dem sie liegt; die Muster aus den
Einstellungen gelten relativ zur Wurzel. Die Wirkung ist additiv:
eine Datei gilt als ausgeschlossen, sobald **eine** Quelle sie matched.
Drittens wirken die **UI-Pausen**: Ordner, die per Rechtsklick im
Folder-Tree pausiert wurden, werden über einen Pfad-Präfix-Check
ebenfalls übergangen.

Eine besondere Rolle spielt die **Negation** mit `!`. Ein Muster ohne
führendes Ausrufezeichen schließt aus, ein Muster mit `!` ist eine
Aufhebung. Drei Beispiele zeigen die Wirkung:

Wer das gesamte Archiv ausschließen möchte, aber das Jahr 2026 weiter
indizieren will, schreibt:

```text
archive/**
!archive/2026/**
```

Wer nur die `README.md` jedes Unterordners aufnehmen will, kann den
Rest komplett ausschließen:

```text
**/*
!**/README.md
```

Und wer eine einzelne Datei trotz eines pauschalen Verbots im Index
behalten will, legt eine `.mdignore` in den Ordner selbst, etwa nach
`drafts/.mdignore`:

```text
*
!important.md
```

Tritt eine Datei in keiner Quelle als ausgeschlossen auf, läuft sie
durch den Parser. Der Parser baut auf Markdig auf und ergänzt eine
eigene **WikiLink-Erweiterung**, die `[[Slug]]`-Notationen erkennt und
bei der späteren HTML-Rendering-Phase in interne Links auflöst. Auch
Frontmatter (YAML-Block am Datei-Anfang) und Hashtags im Fließtext
werden in dieser Phase extrahiert; aus beiden Quellen entsteht die
Tag-Liste der Datei.

## 5. Suche und Vorschau {#suche}

Die Suche basiert auf SQLite mit aktivierter **FTS5-Erweiterung**,
einer Volltext-Suchengine, die für textlastige Bestände auf
Notizbuch-Skala konzipiert ist. Der Vorteil gegenüber einer schlichten
`LIKE`-Suche zeigt sich an drei Stellen: erstens skaliert FTS5 auf
zehntausende Dokumente ohne spürbare Verzögerung, zweitens versteht
es Phrasen-, Wildcard- und Prefix-Anfragen, und drittens liefert es
zusammen mit dem Treffer ein „Snippet", einen kurzen Auszug rund um
die Fundstelle, mit hervorgehobenen Treffer-Begriffen.

Im Suchfeld des Registers „Suche" in der linken Spalte genügt das Tippen,
um eine Suche auszulösen; nach einem konfigurierbaren Debounce
(standardmäßig 300 Millisekunden) feuert die Anfrage. Wer den Fokus
ins Suchfeld setzen möchte, ohne zur Maus zu greifen, erreicht das
mit `Strg+F`; das Register wechselt dabei mit. Nur im Bearbeiten-Modus
des Dokuments sucht `Strg+F` stattdessen im Text der Datei. `Esc` leert
das Suchfeld wieder.

Die Anfrage darf aus mehreren Wörtern bestehen, die implizit mit
„UND" verknüpft werden. Phrasen (Wortfolgen in genau dieser
Reihenfolge) gehören in Anführungszeichen: die Suche `"Domain
Driven Design"` liefert ausschließlich Treffer, in denen alle drei
Wörter in dieser Reihenfolge stehen. Wer den Anfang eines Worts
sucht, ohne die genaue Endung zu kennen, hängt einen Stern an:
`indizi*` findet sowohl `indizieren` als auch `Indizierung`.

Über die beiden Auswahlfelder neben dem Suchfeld wählen Sie den
**Modus** („Volltext“ oder „Regulärer Ausdruck“) und die
**Ähnlichkeit**: „Genau“ sucht die Wörter, wie sie getippt sind,
„Wortstamm“ findet auch gebeugte Formen, „Ähnlicher Wortstamm“
zusätzlich leicht abweichende Schreibweisen, und „Ähnlicher Wortstamm
und Synonyme“ nimmt bedeutungsgleiche Wörter hinzu.

Drei spezielle Filter erweitern die Suche um Metadaten. Der Filter
`tag:projekt` schränkt auf Dateien ein, die den Tag `projekt`
tragen; mehrere Tag-Filter kombinieren sich mit „UND". Der negative
Filter `-tag:archiv` schließt Dateien mit einem bestimmten Tag aus,
hilfreich, um veraltete Notizen aus den Treffern fernzuhalten.
Der Filter `path:notizen/2026` beschränkt auf Dateien unterhalb
eines Pfad-Fragments. Alle Filter lassen sich frei mit Volltext-
Suchbegriffen kombinieren: `tag:rezept salzig -tag:vegetarisch`
findet salzige Rezepte ohne Vegetarisch-Tag.

Unter dem Suchfeld liegt das Kontrollkästchen **„Nur aktueller
Ordner"**. Ohne Haken (der Standard) sucht die Anwendung global
über alle konfigurierten Wurzeln, unabhängig davon, welcher Ordner
links im Baum markiert ist. Mit Haken beschränkt sie die Treffer auf
den aktuell gewählten Ordner und dessen Unterordner; die Auswahl der
Baum-Wurzel selbst hebt die Einschränkung wieder auf (globale Suche).
Im Gegensatz zum `path:`-Filter, den man pro Anfrage tippt, bleibt
diese Einstellung über alle Suchen hinweg aktiv, bis man sie wieder
abwählt. Das Umschalten löst die laufende Suche sofort neu aus, ebenso
die Wahl eines anderen Ordners im Baum, solange der Haken gesetzt ist.
Die Beschränkung wirkt in der Abfrage selbst: Auch Treffer, die über
alle Wurzeln betrachtet weit hinten lägen, erscheinen vollständig.
Wie viele Treffer eine Suche holt, bestimmt die Einstellung „Treffer
pro Seite" unter Darstellung.

Jeder Eintrag der Trefferliste zeigt einen Ausschnitt aus dem Fundstück, in
dem die **Trefferstellen hervorgehoben** sind. Die Hervorhebung folgt dem
gewählten Erscheinungsbild und bleibt in beiden lesbar.

Ein Klick auf einen Treffer öffnet die Datei in der rechten Spalte.
Die Vorschau rendert das Markdown in einer eingebetteten WebView2-
Instanz. Frontmatter-Blöcke werden ausgeblendet, Code-Blöcke in einer
Festbreitenschrift abgesetzt dargestellt (ohne Syntaxfärbung: die Vorschau
führt aus Sicherheitsgründen kein Skript aus), und WikiLinks der Form
`[[Notiz-Slug]]` werden als interne Links auf die Zieldatei aufgelöst; ein
Klick darauf wechselt die Vorschau auf das Verlinkungsziel. Bilder werden
relativ zur Quelldatei aufgelöst, auch mit `..` in einen Nachbarordner,
sodass Markdown-Notizen mit Bildordnern unverändert dargestellt werden.
Ein Pfad mit führendem `/` meint die Index-Wurzel. Was über die Wurzel
hinausführt, zeigt die Vorschau nicht; für eine Datei, die nicht im Index
steht, gilt ihr eigener Ordner als Grenze.

Verweise ins Netz (`https://…`, `http://…`) öffnen im Standardbrowser,
`mailto:`-Verweise im Mailprogramm. In der Vorschau selbst bleibt immer
die Notiz stehen. Andere Verweisarten, etwa auf lokale Dateien
(`file:`), führt die Vorschau nicht aus.

Bilder von fremden Servern bleiben dabei aus. Der Schalter dafür heißt
„Bilder aus dem Netz in der Vorschau laden" und steht unter
`Einstellungen → Verhalten`. Der Grund: Ein Netzbild ruft beim Anzeigen einen
fremden Server auf und verrät ihm, wann Sie welche Notiz geöffnet haben. Wer
Abzeichen und Netzbilder sehen will, schaltet ihn ein; geladen werden dann
ausschließlich Bildquellen, Skripte bleiben in jedem Fall gesperrt.

Wer auf den Rohtext zugreifen möchte, sei es, um einen Schreibfehler
zu korrigieren oder einen Tag zu ergänzen, wechselt mit `Strg+E`
oder dem Toolbar-Button auf den Bearbeiten-Modus. Eine Monospace-
TextBox erlaubt dann direkte Änderungen; `Strg+S` schreibt sie
atomar zurück in die Datei. Das ursprüngliche Zeilenende (CRLF
unter Windows, LF aus einem Linux-Editor) bleibt erhalten, sodass
sich Konflikte mit Versionskontroll-Systemen vermeiden lassen.

Stehen ungespeicherte Änderungen an und Sie öffnen eine andere Datei,
benennen die offene Datei um, verschieben oder löschen sie oder
schließen das Programm, fragt MdExplorer nach: „Ja“ speichert, „Nein“
verwirft die Änderungen, „Abbrechen“ lässt Sie beim Dokument. Escape
wirkt wie Abbrechen.

## 6. Tag-Cloud und Tag-Verwaltung {#tagcloud}

Tags sind in MdExplorer eine zweite Klassifikations-Schiene neben
dem Ordnerbaum. Eine Datei kann beliebig viele Tags tragen, und ein
Tag kann beliebig viele Dateien zusammenfassen. MdExplorer kennt
zwei Quellen für Tags. Die erste ist der **Frontmatter-Block** am
Anfang einer Datei: enthält er ein `tags:`-Feld als Liste oder
kommaseparierten String, fließen seine Einträge in den Index.
Die zweite Quelle sind **Inline-Hashtags** im Fließtext der Datei,
also Sequenzen der Form `#thema`, `#projekt-x`, `#2026-q2`. Die
Hashtag-Extraktion lässt sich im Einstellungs-Dialog deaktivieren,
sinnvoll für Bestände, in denen `#` häufig in Code-Blöcken oder als
Markdown-Überschrift auftritt; Frontmatter-Tags bleiben in dem Fall
unberührt.

Die **Tag-Cloud** in der optionalen vierten Spalte zeigt eine
visuelle Übersicht der häufigsten Tags. Die Schriftgröße eines Tags
skaliert **logarithmisch** mit seiner Verwendungshäufigkeit, sodass
ein dreißigfach genutzter Tag rund doppelt so groß erscheint wie ein
fünfmal genutzter; die lineare Skalierung würde häufige Tags
unhandlich groß darstellen. In der Panel-Kopfzeile lassen sich
Sortierreihenfolge (Häufigkeit, Alphabetisch, Zuletzt verwendet) und
ein „Long-Tail-Modus" umschalten, der auch selten verwendete Tags
einblendet.

Ein **Klick auf einen Tag** schreibt `tag:<slug>` ins Suchfeld und
löst eine Suche aus. Die Trefferliste zeigt anschließend alle
Dateien mit diesem Tag. Mit gedrückter `Strg`-Taste verhält sich der
Klick additiv: der Filter wird an die bestehende Suche angehängt, so
lassen sich mehrere Tags kombinieren. Mit `Alt` wird der Tag
ausgeschlossen (`-tag:<slug>`), was etwa bei „alles außer Archiv"
hilft.

Wer Tags umbenennen, zusammenführen oder löschen möchte, öffnet über
den Tag-Knopf in der Kopfleiste einen modalen Dialog. Er listet alle
Tags des Bestands mit der jeweiligen Anzahl betroffener Dateien.
Drei Operationen stehen zur Auswahl. Beim **Umbenennen** werden
alle Vorkommen `#alt` durch `#neu` ersetzt, sowohl im Fließtext als
auch im Frontmatter. Beim **Zusammenführen** verschwindet der
Quell-Tag, seine Dateien tragen statt seiner den Ziel-Tag; doppelte
Einträge im Frontmatter werden automatisch entfernt. Beim **Löschen**
werden sämtliche Vorkommen aus Fließtext und Frontmatter entfernt.

Vor jeder Operation zeigt ein Bestätigungsdialog die Anzahl der
betroffenen Dateien und die ersten zehn Pfade — ein Sicherheitsnetz,
das versehentliche Massen-Umschreibungen verhindert. Schreibvorgänge
erfolgen atomar; der `FileSystemWatcher` triggert anschließend
automatisch den Re-Index, sodass Tag-Cloud und Suche innerhalb
weniger Sekunden den neuen Stand zeigen.

## 7. Die Graph-Ansicht {#graph}

Die Graph-Ansicht öffnet sich über den Graph-Knopf in der Kopfleiste in einem
eigenen Fenster und zeigt den WikiLink-Graphen des aktuellen
Bestands. Jeder Knoten ist eine Markdown-Datei, jede Kante ist eine
`[[WikiLink]]`-Referenz aus dem Parser. Die Darstellung erfolgt in
einer eingebetteten WebView2-Instanz, die ein eigenes HTML-, CSS-
und JavaScript-Paket lädt. Alle Assets sind als Embedded Resources
im Anwendungspaket enthalten; es gibt keinen CDN-Aufruf, keinen
Download externer Bibliotheken und keinen Netzwerkverkehr — das
ist ein bewusster Design-Entschluss, der MdExplorer auch in
abgeschotteten Umgebungen einsatzfähig macht.

Die **Größe eines Knotens** richtet sich nach seiner Eingangs-
Verlinkungs-Anzahl: viele andere Dateien verweisen auf ihn, also
wird er größer dargestellt. Diese Skalierung macht zentrale
Notizen sofort sichtbar — eine „Übersicht"- oder „Index"-Datei,
auf die viele Themen-Notizen zeigen, wird zum visuellen Anker.
Knoten ohne eingehende oder ausgehende Verbindungen (sogenannte
„Waisen") sind kleine, isolierte Punkte am Rand des Layouts und
zeigen oft Notizen, die im Zettelkasten nicht angebunden sind.

Der **Graph lässt sich mit Maus und Tastatur bedienen**: Mit der
linken Maustaste zieht man den gesamten Graphen durch den sichtbaren
Bereich oder einen einzelnen Knoten an eine andere Stelle; seine
Nachbarn folgen ihm. Das Mausrad zoomt stufenlos. Ohne Maus erreicht
`Tab` die Zeichenfläche; dort zoomen `+` und `-`, die Pfeiltasten
verschieben, und `0` setzt die Ansicht zurück. Ein Screenreader liest
die Zeichenfläche als Bild mit Knoten- und Kantenzahl und dieser
Bedienung vor. Die Legende in der oberen linken Ecke zeigt die
aktuelle Knoten- und Kantenzahl, sodass auf einen Blick erkennbar
ist, wie groß der Graph gerade ist.

Aus Sicherheitssicht läuft das Graph-Fenster mit einer strikten
Content-Security-Policy: nur ressourcen-eigene Skripte und Stile mit
korrektem Nonce dürfen wirken, externe Quellen sind per
`default-src 'none'` blockiert. Die Übergabe der Graph-Daten ans
JavaScript erfolgt über einen `<script type="application/json">`-
Block, kein Inline-Code, keine Eval-Aufrufe.

## 8. Häufig gestellte Fragen {#faq}

**Wo liegen meine Daten?**
Sämtliche Benutzerdaten (Einstellungen, Datenbank, Logs)
liegen unterhalb von `%LOCALAPPDATA%\MdExplorer\`. Das entspricht
auf einem Standard-Windows-System dem Pfad
`C:\Users\<Benutzer>\AppData\Local\MdExplorer\`. Konkret findet sich
dort die Datei `settings.json` mit den Einstellungen, `ui-layout.json`
mit den Spaltenbreiten, die SQLite-Datenbank `app.db` mit dem
FTS5-Suchindex und ein Verzeichnis `logs\` mit tagesweise rotierten
Logdateien.

**Wie setze ich den Index zurück?**
Ein vollständiger Reset gelingt durch Schließen der Anwendung,
Löschen der Datei `app.db` aus dem Datenverzeichnis und einem
Neustart. Beim nächsten Start legt MdExplorer eine leere Datenbank
an, der Indexer scannt sämtliche Wurzeln neu und füllt den Index
auf. Die `settings.json` bleibt unberührt, also gehen weder Roots
noch Ausschluss-Muster verloren.

**Was tun, wenn die Anwendung abstürzt oder einfriert?**
Der erste Anlaufpunkt ist das Logverzeichnis unter
`%LOCALAPPDATA%\MdExplorer\logs\`. Dort liegen tagesweise rotierte
Logdateien mit Zeitstempel, Log-Level und Ereignis-ID. Einträge der
Stufe `Error` oder `Critical` weisen meist auf die Ursache hin,
etwa eine korrupt gewordene SQLite-Datei oder einen unerreichbaren
Index-Pfad nach einem getrennten Netzlaufwerk. Bei wiederkehrenden
Problemen hilft das Löschen der `app.db`, um eine beschädigte
Datenbank auszuschließen. Für eine genauere Spur lässt sich die
Protokollstufe anheben: mit der Umgebungsvariable
`Serilog__MinimumLevel=Debug` oder einer Datei `appsettings.json`
neben `MdExplorer.exe` mit `{ "Serilog": { "MinimumLevel": "Debug" } }`.
Im Protokollfenster (Protokoll-Knopf in der Kopfleiste) grenzt der Zeitraumfilter die
Einträge auf die letzten 15 Minuten, die letzte Stunde oder die
letzten 24 Stunden ein.

**Welche Markdown-Dialekte werden unterstützt?**
MdExplorer nutzt Markdig mit den Standard-CommonMark-Erweiterungen
sowie der eigenen WikiLink-Erweiterung für `[[Slug]]`-Notation.
Frontmatter im YAML-Format am Datei-Anfang wird erkannt und
ausgewertet, aber nicht in die Vorschau übernommen. Tabellen, Task-
Listen, Code-Blöcke mit Sprachkennzeichnung, Fußnoten und automatische
Link-Erkennung sind verfügbar; sehr exotische Erweiterungen einzelner
Editoren (etwa Obsidian-spezifische Embed-Notationen) werden als
Roh-Text dargestellt.

**Wie kann ich meinen Bestand sichern?**
Es genügt, das Verzeichnis mit den Markdown-Dateien zu sichern.
Index und Einstellungen lassen sich aus den Quelldateien jederzeit
neu aufbauen. Wer auch die Einstellungen und gepausten Ordner
mitsichern möchte, nimmt zusätzlich die `settings.json` aus dem
Datenverzeichnis mit. Die SQLite-Datenbank ist hingegen reine
Cache-Information und muss nicht ins Backup.

**Wie melde ich Fehler oder Verbesserungswünsche?**
Fehlermeldungen mit reproduzierbarer Beschreibung (am besten mit
einem Auszug aus dem aktuellen Log und einer Angabe der eingesetzten
.NET- und Windows-Version) gehen an das Projekt-Repository. Ein
ergänzender Screenshot ist bei UI-Themen hilfreich; bei Indexierungs-
oder Such-Fehlern ist ein anonymisiertes Datei-Beispiel
aussagekräftiger als Worte allein.
