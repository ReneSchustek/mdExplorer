using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdExplorer.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Leere Ausgangsmigration: Die Tabellen legen die Folge-Migrationen an, beginnend mit
            // AddMarkdownFile. Diese hier gibt ihnen nur einen Bezugspunkt.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Up legt nichts an, also gibt es nichts zurückzunehmen.
        }
    }
}
