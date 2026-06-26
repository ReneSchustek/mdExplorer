using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdExplorer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFts5Index : Migration
    {
        private const string CreateVirtualTableSql = """
            CREATE VIRTUAL TABLE "MarkdownSearchIndex" USING fts5(
                "Title",
                "Body",
                "Tags",
                "Frontmatter",
                "Path" UNINDEXED,
                "MarkdownFileId" UNINDEXED,
                "SourceContentHash" UNINDEXED,
                tokenize = 'unicode61 remove_diacritics 2'
            );
            """;

        private const string DropVirtualTableSql = """
            DROP TABLE IF EXISTS "MarkdownSearchIndex";
            """;

        private const string CreateDeleteDocumentTriggerSql = """
            CREATE TRIGGER "trg_MarkdownDocuments_AD_FtsCleanup"
            AFTER DELETE ON "MarkdownDocuments"
            BEGIN
                DELETE FROM "MarkdownSearchIndex"
                WHERE "MarkdownFileId" = OLD."MarkdownFileId";
            END;
            """;

        private const string DropDeleteDocumentTriggerSql = """
            DROP TRIGGER IF EXISTS "trg_MarkdownDocuments_AD_FtsCleanup";
            """;

        private const string CreateDeleteFileTriggerSql = """
            CREATE TRIGGER "trg_MarkdownFiles_AD_FtsCleanup"
            AFTER DELETE ON "MarkdownFiles"
            BEGIN
                DELETE FROM "MarkdownSearchIndex"
                WHERE "MarkdownFileId" = OLD."Id";
            END;
            """;

        private const string DropDeleteFileTriggerSql = """
            DROP TRIGGER IF EXISTS "trg_MarkdownFiles_AD_FtsCleanup";
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.Sql(CreateVirtualTableSql);
            _ = migrationBuilder.Sql(CreateDeleteDocumentTriggerSql);
            _ = migrationBuilder.Sql(CreateDeleteFileTriggerSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.Sql(DropDeleteFileTriggerSql);
            _ = migrationBuilder.Sql(DropDeleteDocumentTriggerSql);
            _ = migrationBuilder.Sql(DropVirtualTableSql);
        }
    }
}
