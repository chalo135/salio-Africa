using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PriceChangeHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PriceChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    OldPriceMinor = table.Column<long>(type: "bigint", nullable: true),
                    NewPriceMinor = table.Column<long>(type: "bigint", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceChanges_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriceChanges_OrganizationId_ChangedAt",
                table: "PriceChanges",
                columns: new[] { "OrganizationId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceChanges_ProductId",
                table: "PriceChanges",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceChanges");
        }
    }
}
