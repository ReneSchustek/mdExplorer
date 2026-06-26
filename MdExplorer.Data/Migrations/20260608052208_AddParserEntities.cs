using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdExplorer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddParserEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.CreateTable(
                name: "MarkdownDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MarkdownFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FrontmatterJson = table.Column<string>(type: "TEXT", nullable: false),
                    OutlinksJson = table.Column<string>(type: "TEXT", nullable: false),
                    ParsedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RenderedHtmlGz = table.Column<byte[]>(type: "BLOB", nullable: false),
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_MarkdownDocuments", x => x.Id);
                });

            _ = migrationBuilder.CreateTable(
                name: "MarkdownFileTags",
                columns: table => new
                {
                    MarkdownFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TagId = table.Column<Guid>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_MarkdownFileTags", x => new { x.MarkdownFileId, x.TagId });
                });

            _ = migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_Tags", x => x.Id);
                });

            _ = migrationBuilder.CreateIndex(
                name: "IX_MarkdownDocuments_MarkdownFileId",
                table: "MarkdownDocuments",
                column: "MarkdownFileId",
                unique: true);

            _ = migrationBuilder.CreateIndex(
                name: "IX_MarkdownFileTags_TagId",
                table: "MarkdownFileTags",
                column: "TagId");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Tags_Slug",
                table: "Tags",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropTable(
                name: "MarkdownDocuments");

            _ = migrationBuilder.DropTable(
                name: "MarkdownFileTags");

            _ = migrationBuilder.DropTable(
                name: "Tags");
        }
    }
}
