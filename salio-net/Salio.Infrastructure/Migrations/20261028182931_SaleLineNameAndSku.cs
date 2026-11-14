using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SaleLineNameAndSku : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProductName",
                table: "SaleLines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Sku",
                table: "SaleLines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // Lines sold before this migration get the name and SKU their
            // part has now. Until this release nothing could rename a part,
            // so these are the names they were sold under.
            migrationBuilder.Sql(
                """
                UPDATE "SaleLines" AS l
                SET "ProductName" = p."Name", "Sku" = p."Sku"
                FROM "Products" AS p
                WHERE p."Id" = l."ProductId"
                  AND p."OrganizationId" = l."OrganizationId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProductName",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "Sku",
                table: "SaleLines");
        }
    }
}
