using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SaleSellerAndSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AppUserId",
                table: "Sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SecurityStamp",
                table: "AppUsers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Sales_AppUserId",
                table: "Sales",
                column: "AppUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_AppUsers_AppUserId",
                table: "Sales",
                column: "AppUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_AppUsers_AppUserId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_AppUserId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "AppUserId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "AppUsers");
        }
    }
}
