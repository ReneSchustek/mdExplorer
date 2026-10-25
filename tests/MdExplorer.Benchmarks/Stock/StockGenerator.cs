using System.Globalization;
using System.Text;

namespace MdExplorer.Benchmarks.Stock;

/// <summary>
/// Erzeugt einen Notizbestand in der Größe und Verteilung des echten Einsatzes, aus einem festen
/// Startwert und damit auf jedem Rechner gleich.
/// </summary>
/// <remarks>
/// <para>
/// Ein mitgelieferter Bestand enthielte fremde Notizen und läge nur auf einem Rechner. Ein
/// erzeugter ist nachvollziehbar und lässt sich überall neu bauen.
/// </para>
/// <para>
/// Die Zahlen folgen dem Bestand, für den das Programm gebaut ist (rund 7.700 Dateien in der
/// Statusleiste des Entwicklerrechners): Die meisten Notizen sind kurz, wenige sind sehr lang,
/// Schlagworte und Verweise sammeln sich auf wenige häufige Ziele. Eine Gleichverteilung würde
/// jede Suche gleich viele Treffer finden lassen; die echte Suche trifft auf häufige und seltene
/// Begriffe, und genau das soll die Messung abbilden.
/// </para>
/// </remarks>
internal static class StockGenerator
{
    /// <summary>Anzahl der Notizen, Größenordnung des echten Bestands.</summary>
    public const int FileCount = 7_700;

    /// <summary>Fester Startwert; derselbe Bestand auf jedem Rechner.</summary>
    public const int Seed = 20_261_024;

    /// <summary>Wort, das in jeder zwanzigsten Notiz steht: ein mittelhäufiger Suchbegriff.</summary>
    public const string MediumFrequencyTerm = "Rechnungsprüfung";

    /// <summary>Wortgruppe, die in jeder fünfzigsten Notiz wörtlich steht.</summary>
    public const string Phrase = "projektweite Suche";

    /// <summary>Häufigstes Schlagwort des Bestands.</summary>
    public const string FrequentTag = "projekt";

    /// <summary>Stamm, der in mehreren Beugungsformen vorkommt (Rechnung, Rechnungen, Rechnungs…).</summary>
    public const string StemmedTerm = "Rechnungen";

    // Rund 2 % sehr lange Notizen (Protokolle, Exporte): Sie prägen die Parse-Zeit stärker als
    // ihre Zahl vermuten lässt.
    private const double LargeShare = 0.02;

    private const int FolderCount = 60;

    // Jede zwanzigste Notiz nennt den mittelhäufigen Begriff, jede fünfzigste die Wortgruppe: rund
    // 385 und 154 Treffer, also mehrere Ergebnisseiten, aber kein Wort, das überall steht.
    private const int MediumTermInterval = 20;
    private const int PhraseInterval = 50;

    // Anteile je Wort: ein Verweis auf rund 250 Wörter, ein Hashtag auf rund 330, eine Beugungsform
    // des Suchstamms auf rund 200. Das ergibt die Größenordnung des echten Bestands, ein bis drei
    // Verweise in einer typischen Notiz.
    private const double LinkShare = 0.004;
    private const double HashtagShare = 0.007;
    private const double InflectionShare = 0.012;

    // Obergrenze für Absätze einer gewöhnlichen Notiz; was länger ist, zählt zu den großen.
    private const int MaxParagraphs = 40;

    // Titel und Ordner ziehen aus den ersten 80 Wörtern, den Hauptwörtern; Füllwörter wie "und"
    // ergäben keine glaubwürdigen Namen.
    private const int NounCount = 80;

    private const int TagPoolSize = 300;

    private static readonly string[] Words =
    [
        "Notiz", "Projekt", "Rechnung", "Kunde", "Angebot", "Besprechung", "Termin", "Aufgabe",
        "Entwurf", "Prüfung", "Ergebnis", "Änderung", "Übersicht", "Anleitung", "Schnittstelle",
        "Datenbank", "Abfrage", "Zugriff", "Fehler", "Lösung", "Ursache", "Größe", "Straße",
        "Bericht", "Protokoll", "Stand", "Planung", "Freigabe", "Lieferung", "Bestellung",
        "Vorlage", "Struktur", "Ordner", "Datei", "Verweis", "Schlagwort", "Suche", "Index",
        "Vorschau", "Einstellung", "Oberfläche", "Fenster", "Knopf", "Liste", "Tabelle",
        "Grafik", "Bild", "Text", "Absatz", "Kapitel", "Abschnitt", "Zeile", "Spalte", "Wert",
        "Grenze", "Messung", "Zeit", "Dauer", "Woche", "Monat", "Quartal", "Jahr", "Budget",
        "Kosten", "Preis", "Rabatt", "Steuer", "Zahlung", "Konto", "Beleg", "Archiv", "Version",
        "Fassung", "Umzug", "Server", "Netzwerk", "Sicherung", "Wiederherstellung", "Zugang",
        "schnell", "langsam", "offen", "erledigt", "wichtig", "dringend", "später", "neu", "alt",
        "groß", "klein", "einfach", "schwierig", "geprüft", "fertig", "bereit", "gültig",
        "prüfen", "ändern", "anlegen", "löschen", "suchen", "finden", "lesen", "schreiben",
        "speichern", "öffnen", "schließen", "verschieben", "umbenennen", "freigeben", "planen",
        "und", "oder", "aber", "weil", "dass", "wenn", "mit", "ohne", "für", "über", "unter",
        "nach", "vor", "bei", "von", "zu", "im", "am", "der", "die", "das", "ein", "eine",
    ];

    private static readonly string[] InflectedForms = ["Rechnung", "Rechnungen", "Rechnungslauf"];

    /// <summary>Schreibt den Bestand nach <paramref name="rootPath"/> und liefert die Zahl der Dateien.</summary>
    /// <param name="rootPath">Leerer Zielordner.</param>
    /// <returns>Anzahl der geschriebenen Notizen.</returns>
    public static int Write(string rootPath)
    {
        SplitMixRandom random = new(Seed);
        string[] tags = BuildTagPool();
        string[] titles = BuildTitles(random);
        string[] folders = BuildFolders(random);

        for (int index = 0; index < FileCount; index++)
        {
            string folder = Path.Combine(rootPath, folders[Zipf(random, folders.Length)]);
            _ = Directory.CreateDirectory(folder);
            string content = BuildNote(random, index, titles, tags);
            File.WriteAllText(Path.Combine(folder, titles[index] + ".md"), content, Encoding.UTF8);
        }

        return FileCount;
    }

    private static string BuildNote(SplitMixRandom random, int index, string[] titles, string[] tags)
    {
        StringBuilder note = new();
        AppendFrontmatter(note, random, tags);
        _ = note.Append("# ").AppendLine(titles[index]).AppendLine();

        bool large = random.NextDouble() < LargeShare;
        int paragraphs = large ? random.Next(150, 400) : LogNormalParagraphs(random);
        for (int paragraph = 0; paragraph < paragraphs; paragraph++)
        {
            AppendParagraph(note, random, titles, tags);
        }

        if (index % MediumTermInterval == 0)
        {
            _ = note.Append("Offen ist die ").Append(MediumFrequencyTerm).AppendLine(" für den Kunden.");
        }

        if (index % PhraseInterval == 0)
        {
            _ = note.Append("Die ").Append(Phrase).AppendLine(" findet alles, was hier steht.");
        }

        return note.ToString();
    }

    // Null bis vier Schlagworte im Frontmatter, gezogen nach Häufigkeit: ein paar Schlagworte
    // tragen den Großteil, viele kommen nur selten vor.
    private static void AppendFrontmatter(StringBuilder note, SplitMixRandom random, string[] tags)
    {
        int count = random.Next(0, 5);
        if (count == 0)
        {
            return;
        }

        _ = note.AppendLine("---").Append("tags: [");
        for (int i = 0; i < count; i++)
        {
            _ = note.Append(i == 0 ? string.Empty : ", ").Append(tags[Zipf(random, tags.Length)]);
        }

        _ = note.AppendLine("]").AppendLine("---").AppendLine();
    }

    private static void AppendParagraph(StringBuilder note, SplitMixRandom random, string[] titles, string[] tags)
    {
        int words = random.Next(25, 90);
        for (int word = 0; word < words; word++)
        {
            double roll = random.NextDouble();
            if (roll < LinkShare)
            {
                // Verweise gehen bevorzugt auf wenige zentrale Notizen, wie Übersichtsseiten.
                _ = note.Append("[[").Append(titles[Zipf(random, titles.Length)]).Append("]] ");
            }
            else if (roll < HashtagShare)
            {
                _ = note.Append('#').Append(tags[Zipf(random, tags.Length)]).Append(' ');
            }
            else if (roll < InflectionShare)
            {
                _ = note.Append(InflectedForms[random.Next(InflectedForms.Length)]).Append(' ');
            }
            else
            {
                _ = note.Append(Words[Zipf(random, Words.Length)]).Append(' ');
            }
        }

        _ = note.AppendLine().AppendLine();
    }

    // Lognormal um den Median von vier Absätzen (rund 1.500 Zeichen), gedeckelt bei 40.
    private static int LogNormalParagraphs(SplitMixRandom random)
    {
        double normal = Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble());
        int paragraphs = (int)Math.Round(Math.Exp(Math.Log(4.0) + (0.8 * normal)));
        return Math.Clamp(paragraphs, 1, MaxParagraphs);
    }

    // Zieht einen Index mit Zipf-ähnlicher Verteilung: Index 0 ist am häufigsten.
    private static int Zipf(SplitMixRandom random, int count)
    {
        double value = Math.Pow(count + 1, random.NextDouble()) - 1.0;
        return Math.Min((int)value, count - 1);
    }

    private static string[] BuildTagPool()
    {
        string[] tags = new string[TagPoolSize];
        tags[0] = FrequentTag;
        for (int i = 1; i < tags.Length; i++)
        {
            tags[i] = string.Create(CultureInfo.InvariantCulture, $"thema-{i:D3}");
        }

        return tags;
    }

    // Titel mit Leerzeichen und Umlauten wie im echten Bestand; die laufende Nummer hält sie
    // eindeutig, damit jeder Verweis genau ein Ziel hat.
    private static string[] BuildTitles(SplitMixRandom random)
    {
        string[] titles = new string[FileCount];
        for (int i = 0; i < titles.Length; i++)
        {
            string first = Words[random.Next(0, NounCount)];
            string second = Words[random.Next(0, NounCount)];
            titles[i] = string.Create(CultureInfo.InvariantCulture, $"{first} {second} {i:D4}");
        }

        return titles;
    }

    private static string[] BuildFolders(SplitMixRandom random)
    {
        string[] folders = new string[FolderCount];
        for (int i = 0; i < folders.Length; i++)
        {
            int depth = random.Next(1, 4);
            string[] parts = new string[depth];
            for (int level = 0; level < depth; level++)
            {
                parts[level] = string.Create(CultureInfo.InvariantCulture, $"{Words[random.Next(0, NounCount)]}-{i}-{level}");
            }

            folders[i] = Path.Combine(parts);
        }

        return folders;
    }
}
