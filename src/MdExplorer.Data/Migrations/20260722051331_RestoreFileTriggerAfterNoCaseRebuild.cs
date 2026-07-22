using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdExplorer.Data.Migrations
{
    /// <summary>
    /// Legt den beim NOCASE-Rebuild von <c>MarkdownFiles</c> verlorenen FTS5-Cleanup-Trigger neu an.
    /// Bewusst als eigene Migration nach <c>AddAbsolutePathNoCaseCollation</c>: SqlOperation und
    /// Tabellen-Rebuild in derselben Migration wuerden von EF getrennt ausgefuehrt (Rebuild zuletzt),
    /// sodass der Trigger sonst erneut gedroppt wuerde.
    /// </summary>
    public partial class RestoreFileTriggerAfterNoCaseRebuild : Migration
    {
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

            _ = migrationBuilder.Sql(DropDeleteFileTriggerSql);
            _ = migrationBuilder.Sql(CreateDeleteFileTriggerSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Beim Revert dieser Migration verschwindet der Trigger; der Down der vorherigen
            // Migration (Rebuild zurueck auf BINARY) hinterlaesst ohnehin keinen Trigger.
            _ = migrationBuilder.Sql(DropDeleteFileTriggerSql);
        }
    }
}
