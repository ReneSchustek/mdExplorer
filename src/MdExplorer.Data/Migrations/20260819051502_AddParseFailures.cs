using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdExplorer.Data.Migrations
{
    /// <summary>
    /// Legt die Tabelle für Fehlschlag-Vermerke des Parsers an.
    /// </summary>
    /// <remarks>
    /// Eine Datei, die der Parser nicht verarbeiten kann, bekam bis hierher kein Dokument und
    /// galt damit dauerhaft als ungeparst — jeder Durchlauf las sie erneut und schrieb den
    /// vollen Aufrufstapel ins Protokoll. Der Vermerk hält Inhalt und Parser-Fassung fest, an
    /// denen der Versuch gescheitert ist; solange beides gleich bleibt, ruht die Datei.
    /// Der Cascade-Delete räumt den Vermerk mit der Datei weg.
    /// </remarks>
    public partial class AddParseFailures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.CreateTable(
                name: "ParseFailures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MarkdownFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EngineVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    FailedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_ParseFailures", x => x.Id);
                    _ = table.ForeignKey(
                        name: "FK_ParseFailures_MarkdownFiles_MarkdownFileId",
                        column: x => x.MarkdownFileId,
                        principalTable: "MarkdownFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            _ = migrationBuilder.CreateIndex(
                name: "IX_ParseFailures_MarkdownFileId",
                table: "ParseFailures",
                column: "MarkdownFileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropTable(
                name: "ParseFailures");
        }
    }
}
