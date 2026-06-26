using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdExplorer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddParserForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.AddForeignKey(
                name: "FK_MarkdownDocuments_MarkdownFiles_MarkdownFileId",
                table: "MarkdownDocuments",
                column: "MarkdownFileId",
                principalTable: "MarkdownFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            _ = migrationBuilder.AddForeignKey(
                name: "FK_MarkdownFileTags_MarkdownFiles_MarkdownFileId",
                table: "MarkdownFileTags",
                column: "MarkdownFileId",
                principalTable: "MarkdownFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            _ = migrationBuilder.AddForeignKey(
                name: "FK_MarkdownFileTags_Tags_TagId",
                table: "MarkdownFileTags",
                column: "TagId",
                principalTable: "Tags",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropForeignKey(
                name: "FK_MarkdownDocuments_MarkdownFiles_MarkdownFileId",
                table: "MarkdownDocuments");

            _ = migrationBuilder.DropForeignKey(
                name: "FK_MarkdownFileTags_MarkdownFiles_MarkdownFileId",
                table: "MarkdownFileTags");

            _ = migrationBuilder.DropForeignKey(
                name: "FK_MarkdownFileTags_Tags_TagId",
                table: "MarkdownFileTags");
        }
    }
}
