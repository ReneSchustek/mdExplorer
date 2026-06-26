using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdExplorer.Data.Migrations
{
    /// <summary>
    /// Stellt die FTS5-Cleanup-Trigger her, die durch das EF-Core-Recreate-Pattern in
    /// <see cref="AddParserForeignKeys"/> implizit gedroppt wurden. SQLite kennt kein
    /// "ALTER TABLE ADD FOREIGN KEY" — EF baut die Tabelle dafuer neu auf und verliert
    /// dabei alle Trigger. Diese Folge-Migration laeuft nach dem Rebuild und legt sie
    /// idempotent neu an.
    /// </summary>
    public partial class RestoreFtsCleanupTriggersAfterRebuild : Migration
    {
        private const string DropDeleteDocumentTriggerSql =
            """DROP TRIGGER IF EXISTS "trg_MarkdownDocuments_AD_FtsCleanup";""";

        private const string CreateDeleteDocumentTriggerSql = """
            CREATE TRIGGER "trg_MarkdownDocuments_AD_FtsCleanup"
            AFTER DELETE ON "MarkdownDocuments"
            BEGIN
                DELETE FROM "MarkdownSearchIndex"
                WHERE "MarkdownFileId" = OLD."MarkdownFileId";
            END;
            """;

        private const string DropDeleteFileTriggerSql =
            """DROP TRIGGER IF EXISTS "trg_MarkdownFiles_AD_FtsCleanup";""";

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
            _ = migrationBuilder.Sql(CreateDeleteDocumentTriggerSql);
            _ = migrationBuilder.Sql(DropDeleteFileTriggerSql);
            _ = migrationBuilder.Sql(CreateDeleteFileTriggerSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Nach Down sind die Trigger ueberfluessig — die ihnen zugrunde liegenden FKs
            // verschwinden, der Schema-Zustand entspricht wieder dem Pre-AddParserForeignKeys-Stand.
            _ = migrationBuilder.Sql(DropDeleteDocumentTriggerSql);
            _ = migrationBuilder.Sql(DropDeleteFileTriggerSql);
        }
    }
}
