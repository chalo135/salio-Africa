using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SalesAndTaxRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SaleDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EtimsControlNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EtimsSignature = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EtimsQr = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EtimsStatus = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sales", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Band = table.Column<char>(type: "character(1)", nullable: false),
                    RatePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SaleLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPriceMinor = table.Column<long>(type: "bigint", nullable: false),
                    LineTotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    TaxBand = table.Column<char>(type: "character(1)", nullable: false),
                    UnitCostMinor = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleLines_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_OrganizationId_ProductId",
                table: "SaleLines",
                columns: new[] { "OrganizationId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_ProductId",
                table: "SaleLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_SaleId",
                table: "SaleLines",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_OrganizationId_SaleDate",
                table: "Sales",
                columns: new[] { "OrganizationId", "SaleDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_OrganizationId_SaleNumber",
                table: "Sales",
                columns: new[] { "OrganizationId", "SaleNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxRates_OrganizationId_Band_EffectiveFrom",
                table: "TaxRates",
                columns: new[] { "OrganizationId", "Band", "EffectiveFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SaleLines");

            migrationBuilder.DropTable(
                name: "TaxRates");

            migrationBuilder.DropTable(
                name: "Sales");
        }
    }
}
