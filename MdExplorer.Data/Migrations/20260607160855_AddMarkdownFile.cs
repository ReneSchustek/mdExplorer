using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdExplorer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMarkdownFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.CreateTable(
                name: "MarkdownFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AbsolutePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FileNameWithoutExtension = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    LastWriteTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IndexedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_MarkdownFiles", x => x.Id);
                });

            _ = migrationBuilder.CreateIndex(
                name: "IX_MarkdownFiles_AbsolutePath",
                table: "MarkdownFiles",
                column: "AbsolutePath",
                unique: true);

            _ = migrationBuilder.CreateIndex(
                name: "IX_MarkdownFiles_LastWriteTimeUtc",
                table: "MarkdownFiles",
                column: "LastWriteTimeUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropTable(
                name: "MarkdownFiles");
        }
    }
}
