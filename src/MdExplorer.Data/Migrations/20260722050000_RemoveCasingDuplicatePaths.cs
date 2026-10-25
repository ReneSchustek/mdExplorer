using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdExplorer.Data.Migrations
{
    /// <summary>
    /// Entfernt Einträge, deren Pfad sich nur in der Schreibweise von einem anderen unterscheidet,
    /// bevor <see cref="AddAbsolutePathNoCaseCollation"/> den eindeutigen Index unter NOCASE neu
    /// anlegt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Vor der NOCASE-Spalte verglich der Index binär; <c>C:\Notes\Plan</c> und
    /// <c>c:\notes\plan</c> (dieselbe Notiz) konnten nebeneinander stehen. Der Neuaufbau unter NOCASE scheiterte daran
    /// mit „UNIQUE constraint failed“, und die Anwendung startete nicht mehr.
    /// </para>
    /// <para>
    /// Behalten wird je Pfad die zuerst angelegte Zeile (kleinste <c>rowid</c>); Dokumente,
    /// Kennzeichnungen und Volltext der übrigen fallen über die Fremdschlüssel und den damaligen
    /// Aufräum-Auslöser mit weg. Der Indexer legt fehlende Daten beim nächsten Abgleich wieder an.
    /// </para>
    /// <para>
    /// Eine eigene Migration statt einer Zeile in der NOCASE-Migration: Dort zöge EF Core die
    /// SQL-Anweisung hinter den Neuaufbau, und die Bereinigung käme zu spät. Ihre Kennung
    /// liegt zeitlich vor der NOCASE-Migration; Datenbanken, die jene schon haben, bekommen sie
    /// nachträglich und finden nichts zu löschen, weil der Index dort keine Dubletten zulässt.
    /// </para>
    /// </remarks>
    public partial class RemoveCasingDuplicatePaths : Migration
    {
        private const string RemoveDuplicatesSql = """
            DELETE FROM "MarkdownFiles"
            WHERE rowid NOT IN (
                SELECT MIN(rowid)
                FROM "MarkdownFiles"
                GROUP BY "AbsolutePath" COLLATE NOCASE
            );
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            _ = migrationBuilder.Sql(RemoveDuplicatesSql);
        }

        /// <inheritdoc />
        /// <remarks>Gelöschte Dubletten lassen sich nicht zurückholen; der Rückweg tut nichts.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
        }
    }
}
