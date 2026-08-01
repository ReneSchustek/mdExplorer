using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdExplorer.Data.Migrations
{
    /// <summary>
    /// Stellt die <c>AbsolutePath</c>-Spalte auf NOCASE-Collation um, damit Unique-Index und
    /// Punkt-Lookup case-insensitiv arbeiten (Windows-Pfad-Semantik). SQLite kann eine
    /// Spalten-Collation nur per Tabellen-Rebuild ändern — EF baut <c>MarkdownFiles</c> dafür
    /// neu auf und verliert dabei den FTS5-Cleanup-Trigger. Er wird in der Folge-Migration
    /// <c>RestoreFileTriggerAfterNoCaseRebuild</c> neu angelegt (SqlOperation und Rebuild dürfen
    /// nicht in derselben Migration liegen — EF hebt den Rebuild sonst hinter die Sql-Statements).
    /// </summary>
    public partial class AddAbsolutePathNoCaseCollation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.AlterColumn<string>(
                name: "AbsolutePath",
                table: "MarkdownFiles",
                type: "TEXT",
                maxLength: 1024,
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 1024);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.AlterColumn<string>(
                name: "AbsolutePath",
                table: "MarkdownFiles",
                type: "TEXT",
                maxLength: 1024,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 1024,
                oldCollation: "NOCASE");
        }
    }
}
