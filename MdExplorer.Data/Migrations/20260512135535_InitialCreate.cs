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
            // Initial-Snapshot: alle Tabellen werden ueber EnsureCreated()/Model erzeugt.
            // Diese Migration ist Platzhalter, damit Folge-Migrationen einen Bezugspunkt haben.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Initial-Snapshot besitzt kein Rollback-Skript; Schema wird durch Loeschen der
            // SQLite-Datei zurueckgenommen.
        }
    }
}
