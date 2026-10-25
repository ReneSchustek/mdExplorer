# Changelog

Alle nennenswerten Änderungen an diesem Projekt werden in dieser Datei
dokumentiert. Das Format orientiert sich an
[Keep a Changelog](https://keepachangelog.com/de/1.1.0/); die Versionierung
folgt [Semantic Versioning](https://semver.org/lang/de/).

## [Unveröffentlicht]

## [0.16.0] - 2026-10-25

### Hinzugefügt
- Markdown-Dateien lassen sich per Doppelklick öffnen. Das Setup meldet MdExplorer für
  `.md` und `.markdown` unter „Öffnen mit“ an; als Standardprogramm wählt man es dort einmal
  mit „Immer“. Die Datei öffnet auch außerhalb der indizierten Ordner.
- Läuft MdExplorer schon, öffnet eine weitere Datei im vorhandenen Fenster; eine zweite
  Instanz auf derselben Datenbank startet nicht mehr. Ungespeicherte Änderungen werden wie
  beim Wechsel der Datei abgefragt.

### Geändert
- Das Hauptfenster ist neu aufgebaut: eine schlanke Kopfleiste mit Symbolknöpfen statt der
  Menüleiste, die Register links als Segmentleiste, Tags als Chips und eine ruhigere
  Statuszeile. Der lange Pfad darin wird gekürzt und steht vollständig im Tooltip.
- Das Fenster passt sich der Breite an. Unter 960 Pixeln blendet sich die Tag-Cloud aus und
  kommt beim Verbreitern zurück; die linke Spalte und die Tag-Cloud geben bei Platzmangel nach,
  statt am rechten Rand abgeschnitten zu werden.
- Die Tag-Cloud lässt sich über einen Knopf in der Kopfleiste und das ✕ in ihrem Kopf ein-
  und ausblenden.
- Das Protokollfenster filtert zusätzlich nach Zeitraum; die Protokollstufe lässt sich zur
  Diagnose über `appsettings.json` neben dem Programm oder eine Umgebungsvariable anheben.
- Auswahlfelder zeigen deutsche Bezeichnungen („Volltext“, „Wortstamm“, „Wie Windows“) statt
  der Namen aus dem Code.
- Einheitliche Anrede mit „Sie“ und einheitlich „Tag“ statt „Kennzeichnung“.
- Verweise auf Seiten im Netz öffnen aus Handbuch und Graph im Standardbrowser; der
  Update-Verweis wird vor dem Öffnen auf eine Webadresse geprüft.
- Der Graph ist bei hoher Bildschirmskalierung scharf.
- Ein Wechsel von Erscheinungsbild, Schriftgröße oder Wartezeiten liest den Bestand nicht
  mehr komplett neu ein; das tun nur noch Änderungen an der Indexierung.
- Der Über-Dialog nennt alle mitgelieferten Bibliotheken samt Lizenz, darunter YamlDotNet.
- Die Meldungen der Dateivorgänge und der Betriebsanzeige kommen aus der Ressourcendatei.
- README und Handbuch beschreiben die tatsächliche Oberfläche: Suche als Register in der
  linken Spalte; das README erklärt Veröffentlichung und Installer.

### Behoben
- Auswahlfelder zeigen ihren Text vollständig; der Aufklapp-Pfeil lag in schmalen Spalten über
  den letzten Buchstaben. Die Auswahl von Modus und Ähnlichkeit in der Suche bricht bei wenig
  Platz um, statt „Volltext" abzuschneiden.
- Die Schalter „Bearbeiten" und „Tag-Cloud" lassen sich auch mit Screenreader und
  Sprachsteuerung bedienen; bisher wechselte dort nur der Haken, nicht der Zustand.
- Eine falsch gehende Uhr setzt die automatische Update-Prüfung nicht mehr außer Kraft.
- Ein vom Virenscanner gesperrtes Update-Paket führt zu einer Meldung statt zu einem Fehler.
- Ungespeicherte Änderungen gehen nicht mehr still verloren: Vor dem Öffnen einer anderen
  Datei, vor Umbenennen, Verschieben oder Löschen der offenen Datei und beim Schließen fragt
  MdExplorer, ob gespeichert, verworfen oder abgebrochen werden soll.
- Die Schaltfläche „Bearbeiten“ ist nach dem Öffnen einer Datei wieder freigegeben.
- Ein Fehler beim Speichern in die Datenbank, etwa ein doppelter Eintrag oder eine dauerhaft
  belegte Datenbank, beendet Indexierung und Schlagwort-Auswertung nicht mehr für die laufende
  Sitzung; der nächste Durchgang holt nach, was fehlt.
- Suchbegriffe mit Bindestrich oder Punkt wie „E-Mail“, „readme.md“ oder „2026-10-24“ finden
  ihre Dateien wieder, ebenso der Klick auf ein mehrteiliges Schlagwort in der Tag-Cloud.
- „Nur aktueller Ordner“ findet alle Treffer im gewählten Ordner, nicht nur die, die zufällig
  unter den besten der ganzen Sammlung lagen; ein anderer Ordner im Baum sucht sofort neu.
- Die Einstellung „Treffer pro Seite“ wirkt; bisher holte jede Suche höchstens 20 Treffer.
- Eine belegte Datenbank lässt Dateiliste, Suche, Bezüge und Graph nicht mehr endlos laden,
  sondern sagt, dass das Laden gescheitert ist.
- Umbenennen nimmt nur einen Dateinamen an; ein Name mit Ordner oder unzulässigem Zeichen
  verschiebt die Datei nicht mehr unbemerkt. Umbenennen, Verschieben und Löschen wirken nur
  auf die Datei, die gerade zu sehen ist, auch nach einem gescheiterten Laden.
- Eine noch nicht indizierte Datei zu öffnen ersetzt Vorschau, Editor und Bezüge gemeinsam;
  lässt sie sich nicht lesen, bleibt der Bereich leer statt die vorige Datei weiterzuzeigen.
- Der Protokoll-Export auf ein schreibgeschütztes oder gesperrtes Ziel beendet die Anwendung
  nicht mehr, sondern nennt den Grund. Neue Protokollzeilen halten die Anwendung nicht mehr an.
- Das Aufklappen eines Ordners auf einer nicht antwortenden Netzfreigabe friert die Oberfläche
  nicht mehr ein; ein nicht lesbarer Ordner bleibt leer statt einen Fehler zu werfen.
- Lässt sich das Pausieren eines Ordners nicht speichern, sagt die Anwendung es.
- Das Schließen der Einstellungen während der Update-Prüfung endet ohne Fehler.
- Die Dateiliste lädt nach Umbenennen oder Löschen auch dann neu, wenn sie gerade lud.
- Ein unerwarteter Fehler beim Start beendet MdExplorer mit einer Meldung, statt ein
  Startbild ohne Hauptfenster stehen zu lassen.
- Beim Schließen werden die Hintergrunddienste vollständig beendet und das Protokoll zu Ende
  geschrieben, bevor das Programm endet.
- Ein Fehler, der sich wiederholt, zeigt nur noch einen Dialog statt einer Kaskade.
- Fenster, Leisten und Statuszeile übernehmen im dunklen Erscheinungsbild die Farben der
  Anwendung, statt hell zu bleiben; ebenso der Bearbeiten-Knopf und das Kontextmenü des
  Ordnerbaums.
- Mehrere Graph-Fenster stören sich nicht mehr gegenseitig, und ein großer Graph hält die
  Oberfläche beim Laden nicht mehr an.
- Das Handbuch öffnet nach dem Abstecken eines zweiten Bildschirms wieder sichtbar.
- Fehlt die WebView2-Laufzeit, nennen Vorschau, Handbuch und Graph das, statt leer zu bleiben;
  F1 löst dann keinen Fehlerdialog mehr aus.
- Die Suche im Editor meldet die Zeile des Treffers statt einer Zeichenposition.
- Mehrfaches Strg+S startet keinen zweiten Speichervorgang neben dem laufenden.
- Die Schriftgröße der Vorschau aus den Einstellungen wirkt; sie war bisher fest.
- Bilder mit `../` im Pfad, etwa aus einem Nachbarordner, erscheinen in der Vorschau; ein
  Pfad über die Index-Wurzel hinaus zeigt keine gleichnamige falsche Datei mehr.
- Das Fensterlayout wird atomar gespeichert; ein Absturz beim Schließen kostet nicht mehr
  Spaltenbreiten, Hilfefenster und Graph-Filter.
- Eine Datei lässt sich in derselben Schreibweise mit anderer Groß-/Kleinschreibung umbenennen.
- Meldungen nach Umbenennen und Verschieben sind ganze Sätze („ließ sich nicht umbenennen“).
- Lässt sich der Index nach Umbenennen, Verschieben oder Löschen nicht speichern, gilt der
  Vorgang trotzdem und die Meldung sagt, dass der nächste Abgleich nachzieht.
- Eine gescheiterte Update-Prüfung beim Start beendet die Anwendung nicht mehr.
- Ein Tag-Name mit Leerzeichen, Komma oder `>` lässt sich nicht mehr hinzufügen; er hätte den
  verwalteten Tag-Block beschädigt. Der Block übernimmt die Zeilenenden der Datei.
- Fehlt die WebView2-Laufzeit beim ersten Öffnen und wird nachinstalliert, findet das nächste
  Öffnen sie ohne Neustart.
- Die Betriebsanzeige springt bei gleichzeitigen Protokolleinträgen nicht mehr auf Grün,
  während ein Fehler im Beobachtungsfenster liegt.
- Ein relativer Verweis bringt den Verweisöffner nicht mehr zum Werfen.

### Sicherheit
- Ein präpariertes oder kaputtes Frontmatter bringt die Anwendung nicht mehr zum Absturz.
  YAML mit Aliasen, Blöcke über 64 KiB und Verschachtelungen über 64 Ebenen werden
  verworfen; die Notiz wird ohne Frontmatter indiziert und der Grund protokolliert.
- Ein Release entsteht erst, wenn die Lösung gebaut, getestet und die Versionsnummer des
  Tags mit der des Programms gleich ist. Fremde Bausteine der Veröffentlichung sind auf eine
  feste Fassung festgelegt, der Secret-Scanner wird vor dem Start gegen seine Prüfsumme
  geprüft.

- Die Update-Funktion installiert nur noch Setups mit gültiger Signatur des Herausgebers;
  die Prüfsumme allein belegte nur, dass der Download vollständig ist. Sie folgt nur
  Adressen der eigenen Releases-Seite über https, liest Antworten nur bis zu einer festen
  Größe, bricht einen stehenden Download ab und startet nur das gerade geprüfte, unveränderte
  Paket aus einem frischen Ordner.

### Barrierefreiheit
- Alle Knöpfe, Eingabefelder und Auswahlfelder haben für Screenreader einen Namen;
  Listeneinträge, Baumknoten und Tag-Cloud melden ihren Inhalt statt eines internen Typnamens,
  und der Inhalt der Register links ist erreichbar.
- Die Betriebsanzeige in der Statuszeile ist per Tastatur bedienbar und nennt ihren Zustand
  auch als Wort; Such-, Speicher- und Trefferstand werden bei Änderung angesagt.
- Platzhalter, Zeitstempel, Sprungbuchstaben und Schaltflächenränder sind kontrastreicher;
  Verweise in Vorschau und Handbuch sind unterstrichen.
- Der Graph lässt sich per Tastatur zoomen und verschieben und ist für Screenreader
  beschrieben.
- Escape schließt den Einstellungsdialog.

## [0.15.5] - 2026-10-24

Nicht einzeln veröffentlicht; die Änderungen sind in 0.16.0 enthalten.

### Sicherheit
- **Verweise ins Netz öffnen im Standardbrowser, nicht in der Vorschau.** Bisher lud
  ein Klick auf einen `https`-Verweis die fremde Seite in die eingebettete Fläche, ohne
  Adressleiste. Jetzt bleibt dort nur das eigene Dokument; `http`, `https` und `mailto`
  gehen an das Standardprogramm, alles andere (etwa `file:` oder `javascript:`) wird
  verworfen. Dasselbe gilt für Verweise, die ein neues Fenster öffnen wollen.

### Behoben
- Das erste Dokument nach dem Start zeigt seine Bilder; bisher fehlten sie, bis Datei
  oder Theme wechselten.
- Im Einstellungsdialog überlagerten sich der Schalter für Bilder aus dem Netz und der
  Update-Abschnitt.
- Ein Klick auf einen WikiLink öffnet die Datei auch dann, wenn ihr Name Leerzeichen oder
  Satzzeichen enthält. Bei gleichnamigen Notizen öffnet er dieselbe Datei, auf die die
  Kante im Graphen zeigt; zwei Schreibweisen desselben Verweises zählen dort nur einmal.
- Projektweites Umbenennen oder Löschen eines Schlagworts lässt Rauten in Codeblöcken,
  Inline-Code und Verweiszielen unberührt. In der `tags:`-Zeile behalten die übrigen
  Einträge ihre Anführungszeichen und Abstände.
- Suchanfragen mit Ausschluss (`-begriff`) am Anfang, nach `OR` oder als einziger Begriff
  und Anfragen mit einem `AND`/`OR`/`NOT` am Ende führen nicht mehr zu einem Fehler.
- Im RegEx-Modus sind spätere Ergebnisseiten vollständig.
- Die eingestellte Wartezeit der Suche wirkt (ab Werk 300 ms; bisher galten fest 200 ms).
- „Heute“, „7 Tage“ und „30 Tage“ rechnen nach Ortszeit statt nach UTC.
- Die Tag-Cloud zeigt häufige Schlagworte größer als seltene; zugeklappt erscheinen nur
  die häufigsten statt der ganzen langen Liste.
- Das Hilfefenster folgt bei der Einstellung „System“ dem dunklen Erscheinungsbild.
- Nach dem Speichern springt die Vorschau beim Wechsel des Erscheinungsbilds nicht mehr auf
  den alten Text zurück. Eine Datei, die noch nicht im Index steht, zeigt die Bilder ihres
  eigenen Ordners.
- Schließen des Hauptfensters oder ein Reiterwechsel löscht nicht mehr die Geometrie des
  Hilfefensters und den Pfad-Filter des Graphen.
- `.mdignore`-Dateien in Unterordnern wirken relativ zu ihrem eigenen Ordner, wie eine
  `.gitignore`.
- Nach einem kurzen Datenbankfehler läuft der regelmäßige Abgleich weiter; bisher stand er
  bis zum Neustart.
- Speichert ein Editor über eine Umbenennung, bleibt kein verwaister Eintrag zurück, und der
  neue Inhalt wird übernommen.
- Ein Indexordner, dessen Schreibweise in den Einstellungen von der auf der Platte abweicht,
  führt nicht mehr zu doppelten Einträgen.
- Eine beim Indizieren gesperrte Datei wird nach Ende der Sperre wieder im Volltext
  gefunden, spätestens nach etwa fünf Minuten.
- Der Editor zeigt keine Rauten aus dem Frontmatter mehr als Schlagwort an.
- Ein Text, den der Parser abweist, lässt weder Speichern noch Öffnen scheitern; eine
  Layout-Datei ohne Leserecht verhindert den Start nicht mehr.
- Werte außerhalb ihrer Grenzen in einer von Hand bearbeiteten Einstellungsdatei fallen
  beim Start auf die Vorgabe zurück.
- Im Graphen folgen die Nachbarn einem gezogenen Knoten auch dann, wenn das Bild schon zur
  Ruhe gekommen war. Fehlt die WebView2-Laufzeit, bleibt das Graph-Fenster leer, statt
  einen Fehlerdialog auszulösen.
- Unbehandelte Fehler und dauerhaft unlesbare Dateien stehen nur noch einmal mit vollem
  Stapel im Protokoll.

### Geändert
- Neues Messprojekt für die Geschwindigkeit von Indexlauf, Suche, Graph und
  Schlagwort-Statistik; Wächter im Testlauf prüfen, dass die häufigsten Abfragen einen
  Index nutzen.
- Kommentare im Quelltext überarbeitet: Jeder Typ sagt, wofür es ihn gibt, gewählte
  Grenzwerte nennen ihren Grund, und veraltete Aussagen sind berichtigt. Keine
  Änderung am Verhalten.
- Die Beschriftungen der Oberfläche kommen aus Ressourcendateien
  (`Strings/Resources.resx` in App und Tag-Cloud) statt fest aus Markup und
  ViewModels. Die Texte selbst sind unverändert; eine weitere Sprache lässt sich
  damit als eigene Datei ergänzen.

## [0.15.4] - 2026-08-19

### Behoben
- **Eine einzige unlesbare Datei füllte das Protokoll.** Sie wurde alle fünf
  Sekunden erneut ausgewertet, und jeder Versuch schrieb den vollen Fehlerbericht
  mit. Echte Meldungen rollten dadurch binnen Stunden aus den Dateien heraus.
  Ein gescheiterter Versuch wird jetzt vermerkt: Die Datei ruht, solange sie
  unverändert bleibt und dieselbe Programmfassung läuft. Ändert sie sich, läuft
  der Versuch normal wieder an, und der Vermerk verschwindet, sobald sie sich
  auswerten lässt.

### Geändert
- Der Betriebs-Status nennt jetzt, wie viele Dateien nicht verarbeitet werden
  können, und steht dabei mindestens auf Gelb. Vorher verschwand eine dauerhaft
  unlesbare Datei mit ihrem Protokolleintrag aus dem Blick.

## [0.15.3] - 2026-08-18

### Behoben
- **Das Handbuch versprach „kein Aufruf externer Server".** Das war falsch: Die
  Prüfung auf eine neue Fassung fragt beim Start die Veröffentlichungsliste des
  Projekts auf GitHub ab, und sie ist ab Werk eingeschaltet. Abschnitt 1 benennt
  jetzt beide Wege nach draußen, also diese Prüfung und die Bilder von fremden
  Servern in der Vorschau, samt Drossel, Schalter und Werkseinstellung.

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
  Programmdatei ab, nach einer Installation also in einem Ordner, in den kein
  Benutzer schreiben darf. Sie benutzen jetzt dasselbe Verzeichnis wie die
  Vorschau, unterhalb der übrigen Anwendungsdaten. Wer die Anwendung aus dem
  Entwicklungsordner startete, hat davon nie etwas gemerkt.

### Geändert
- Die Verknüpfung auf dem Schreibtisch ist im Setup vorausgewählt und weiterhin
  abwählbar. Bisher war das Häkchen voreingestellt leer. Wer es übersah, hatte
  keine Verknüpfung, und weil das Setup die getroffene Auswahl merkt, blieb das
  auch nach jeder neuen Fassung so.

## [0.15.1] - 2026-08-16

Das Etikett zu dieser Fassung wurde erst am 18.08.2026 nachgetragen; die Pakete im
Verzeichnis der Veröffentlichungen sind darum jünger als die Änderungen selbst. Als
neueste Fassung gilt sie nicht.

### Behoben
- **Bilder in Notizen waren in der Vorschau nie zu sehen.** Ein Bild mit
  relativem Pfad, also praktisch jedes, hatte nichts, worauf es sich beziehen
  konnte: Die Vorschau bekommt ihr HTML als Zeichenkette, und darin gibt es keinen
  Ordner. Jetzt zeigen relative Pfade auf den Ordner des angezeigten Dokuments.

### Hinzugefügt
- Einstellungen → Verhalten: „Bilder aus dem Netz in der Vorschau laden".
  **Ab Werk aus**, damit die Zusage stimmt, dass die Anwendung ohne
  Internetverbindung vollständig arbeitet. Ein Bild von einem fremden Server
  verrät ihm, wann Sie welche Notiz geöffnet haben. Eingeschaltet erscheinen auch
  Abzeichen und Bilder aus dem Netz; geöffnet wird dabei ausschließlich die
  Bildquelle, Skripte bleiben in jedem Fall gesperrt.

## [0.15.0] - 2026-08-16

### Geändert
- `vendor` und die Kernverzeichnisse fremder Systeme lassen sich über die
  Ausschlussmuster aus der Indizierung nehmen, und was bereits im Bestand steht,
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
  Trefferliste, sondern mit dem nächsten Abgleich. Das ist dieselbe Frist von wenigen
  Sekunden, die für neue Dateien ohnehin gilt.
- Eine Kennzeichnung, an der keine Datei mehr hängt, blieb für immer im Bestand
  stehen. Sie wird jetzt weggeräumt.

## [0.14.1] - 2026-08-16

### Behoben
- Ein Filter mit mehreren Wörtern in Anführungszeichen suchte etwas anderes als
  angegeben. `tag:"zwei Wörter"` wurde als Schlagwort `zwei` gelesen, dazu das
  Wort `Wörter` irgendwo im Text; `path:"mein Ordner"` suchte einen Pfad, der mit
  einem Anführungszeichen beginnt.
- Ein Verweis mit einem Ziel, aus dem sich kein Sprungziel bilden lässt (etwa
  `[[…]]` mit drei Punkten als Auslassungszeichen), ließ die **ganze Datei** aus
  dem Bestand fallen. Ein unbrauchbares Ziel bleibt jetzt einfach ein Text.
- Der Aufräumdurchgang der Indizierung entfernt verschwundene Dateien auch dann,
  wenn beim Schreiben eines Stapels etwas schiefging. Bisher hing er am Erfolg
  des gesamten Durchlaufs, und Einträge zu längst gelöschten Dateien blieben stehen.
  Bei einem nur teilweise lesbaren Verzeichnisbaum wird weiterhin nichts
  entfernt: Was niemand gesehen hat, ist nicht dasselbe wie gelöscht.

### Geändert
- Der Bereich „Zusammenhänge" lädt beim Klick auf ein Dokument nur noch dessen
  Nachbarschaft statt des gesamten Bestands.
- `vendor` ist ab Werk von der Indizierung ausgenommen. In diesem Ordner legen PHP,
  Ruby und Go fremde Pakete ab. In einem gewachsenen Bestand lagen dort
  3.906 Markdown-Dateien fremder Dokumentation, die jede Trefferliste überlagern.

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
  Vor dem Eingriff steht, wie viele Dokumente danach ins Leere zeigen, und zwar
  vorher, nicht hinterher.
- Dunkles Erscheinungsbild für die gesamte Oberfläche: Register, Menü,
  Schaltflächen, Titelleiste, Auswahl, Vorschau und Schlagwortverwaltung.

### Behoben
- Die Trefferstellen der Suche waren im dunklen Erscheinungsbild nicht lesbar:
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

Diese Fassung enthält keine Änderung am Funktionsumfang.

### Geändert
- Die Wege, die im Betrieb still fehlschlagen, sind abgesichert: abgebrochene
  Ladevorgänge, eine kurzzeitig belegte Datenbank, gesperrte oder inzwischen
  gelöschte Dateien sowie eine nicht schreibbare Einstellungsdatei. In all
  diesen Fällen bleibt der zuletzt angezeigte Stand stehen, statt zu leeren.
- Der periodische Abgleich des Indexers und das Wiederholen beim Lesen sind
  jetzt durch Tests abgesichert. Beide sorgen dafür, dass eine Datei auch dann
  im Bestand landet, wenn die Dateisystem-Überwachung ihr Entstehen nicht
  gemeldet hat, etwa bei einem zwischenzeitlich getrennten Netzlaufwerk.

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
  wiederhergestellt, Code und Sicherheit gründlich durchgesehen sowie
  Datenschicht-Optimierung
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

[Unveröffentlicht]: https://github.com/ReneSchustek/mdExplorer/compare/v0.16.0...HEAD
[0.16.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.16.0
[0.15.5]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.16.0
[0.15.4]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.15.4
[0.15.3]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.15.3
[0.15.2]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.15.2
[0.15.1]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.15.1
[0.15.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.15.0
[0.14.1]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.14.1
[0.14.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.14.0
[0.13.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.13.0
[0.12.1]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.12.1
[0.12.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.12.0
[0.11.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.11.0
[0.10.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.10.0
[0.9.0]: https://github.com/ReneSchustek/mdExplorer/releases/tag/v0.9.0
