# MdExplorer.Benchmarks

Misst die Wege, die bei einem großen Notizbestand Zeit kosten: den ersten Indexlauf über
alle Dateien, den periodischen Abgleich über einen unveränderten Bestand, das Parsen einer
Notiz, die Suche in ihren Spielarten, den Graphen über alle Dateien und die
Schlagwort-Statistik der Tag-Cloud. Gemessen wird mit BenchmarkDotNet über
die echten Klassen der Anwendung, gegen eine SQLite-Datei auf der Platte.

Das Projekt ist kein Testprojekt. `dotnet test` führt es nicht aus, weil ein Lauf Minuten
dauert und eine ruhige Maschine braucht. Die Solution baut es mit, damit es nicht veraltet.

## Messbestand

Der Bestand wird beim ersten Lauf aus einem festen Startwert erzeugt: 7.700 Notizen mit
Frontmatter-Schlagworten, Hashtags und WikiLinks, überwiegend kurz, einige sehr lang. Er ist
auf jedem Rechner gleich und enthält keine echten Notizen. Erzeugt und indiziert liegt er
unter `%TEMP%\MdExplorer.Benchmarks\` und wird von späteren Läufen wiederverwendet; neue
Migrationen werden dabei vor jeder Messung nachgezogen.

## Starten

```powershell
dotnet run -c Release --project tests/MdExplorer.Benchmarks -- --filter * --artifacts <ordner>
```

Ohne `--filter` fragt das Programm, welche Messklasse laufen soll. Während einer Messung
sollte nichts anderes rechnen; ein paralleler Build verfälscht die Zahlen.

## Ergebnisse

Die Ergebnisse gehören nicht ins Repository: Sie hängen am Rechner, und Zahlen von
verschiedenen Rechnern lassen sich nicht vergleichen. Jeder Lauf wird mit `--artifacts`
in der Arbeitsablage abgelegt, in einem Ordner je Datum und Rechner, zusammen mit der
gemessenen Fassung. Erst die Reihe mehrerer Läufe zeigt, ob etwas langsamer wird.

## Wächter im normalen Testlauf

Die groben Wächter laufen in `MdExplorer.Data.Tests` (`QueryPlanGuardTests`). Sie prüfen
ohne Stoppuhr, dass die heißen Abfragen einen Index nutzen und dass Graph und
Schlagwort-Statistik mit einer Abfrage auskommen, gleich wie groß der Bestand ist.
