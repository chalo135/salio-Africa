using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ImportStockBasis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaidWith",
                table: "ImportBatches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StockBasis",
                table: "ImportBatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill. Every existing batch is now None (0). Until this
            // migration the import could only ever post opening stock, one
            // journal entry per part with SourceType StockAdjustment (3) and
            // SourceId the ImportBatchChange id (decision D4). A batch with
            // at least one such entry posted opening stock, so it becomes
            // OpeningStock (1). PaidWith stays null: no batch was a purchase.
            //
            // Done in two steps on purpose. Step 1 only READS the journal,
            // into a temporary list of batch ids. Step 2 writes only
            // ImportBatches. No statement both names a journal table and
            // writes, so locked rule 3 (and its guard test) holds as it is.
            migrationBuilder.Sql(@"
CREATE TEMP TABLE ""OpeningStockBatchIds"" AS
SELECT DISTINCT c.""ImportBatchId"" AS ""Id"", c.""OrganizationId""
FROM ""ImportBatchChanges"" AS c
JOIN ""JournalEntries"" AS e
    ON e.""SourceId"" = c.""Id""
    AND e.""OrganizationId"" = c.""OrganizationId""
WHERE e.""SourceType"" = 3;");

            migrationBuilder.Sql(@"
UPDATE ""ImportBatches"" AS b
SET ""StockBasis"" = 1
FROM ""OpeningStockBatchIds"" AS o
WHERE o.""Id"" = b.""Id""
    AND o.""OrganizationId"" = b.""OrganizationId"";");

            migrationBuilder.Sql(@"DROP TABLE ""OpeningStockBatchIds"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaidWith",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "StockBasis",
                table: "ImportBatches");
        }
    }
}
