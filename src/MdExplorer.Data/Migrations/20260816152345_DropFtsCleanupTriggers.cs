using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdExplorer.Data.Migrations
{
    /// <summary>
    /// Entfernt die beiden Aufräum-Auslöser auf der Volltext-Tabelle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Die Auslöser räumten nach jedem Löschen einer Datei die zugehörige Zeile aus
    /// <c>MarkdownSearchIndex</c>. Der Gedanke war richtig, der Preis nicht: Die Spalte
    /// <c>MarkdownFileId</c> ist in der FTS5-Tabelle <c>UNINDEXED</c>, jede Bedingung darauf
    /// liest also die ganze Tabelle. Gemessen am 16.08.2026 über einen Bestand von 29.889
    /// Dateien: <b>rund 170 ms je gelöschter Datei</b>. Für die 25.000 Einträge, die nach dem
    /// Setzen der Ausschlussmuster wegfallen mussten, wären das über eine Stunde gewesen —
    /// dieselben 200 Dateien ohne Auslöser und mit einer Anweisung je Portion: 0,3 Sekunden.
    /// </para>
    /// <para>
    /// Die Zeilen bleiben trotzdem nicht stehen: Der Volltext-Abgleich läuft alle fünf
    /// Sekunden und entfernt, was zu keiner Datei mehr gehört. Damit gilt fürs Entfernen
    /// dieselbe Frist wie fürs Hinzufügen — ein Fenster von Sekunden, kein Dauerzustand.
    /// </para>
    /// </remarks>
    public partial class DropFtsCleanupTriggers : Migration
    {
        private const string DropDeleteDocumentTriggerSql =
            """DROP TRIGGER IF EXISTS "trg_MarkdownDocuments_AD_FtsCleanup";""";

        private const string DropDeleteFileTriggerSql =
            """DROP TRIGGER IF EXISTS "trg_MarkdownFiles_AD_FtsCleanup";""";

        private const string CreateDeleteDocumentTriggerSql = """
            CREATE TRIGGER "trg_MarkdownDocuments_AD_FtsCleanup"
            AFTER DELETE ON "MarkdownDocuments"
            BEGIN
                DELETE FROM "MarkdownSearchIndex"
                WHERE "MarkdownFileId" = OLD."MarkdownFileId";
            END;
            """;

        private const string CreateDeleteFileTriggerSql = """
            CREATE TRIGGER "trg_MarkdownFiles_AD_FtsCleanup"
            AFTER DELETE ON "MarkdownFiles"
            BEGIN
                DELETE FROM "MarkdownSearchIndex"
                WHERE "MarkdownFileId" = OLD."Id";
            END;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.Sql(DropDeleteDocumentTriggerSql);
            _ = migrationBuilder.Sql(DropDeleteFileTriggerSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.Sql(CreateDeleteDocumentTriggerSql);
            _ = migrationBuilder.Sql(CreateDeleteFileTriggerSql);
        }
    }
}
