using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PeopleAndShopSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HoldsStock",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemWord",
                table: "Organizations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Parts");

            migrationBuilder.AddColumn<int>(
                name: "TaxPosition",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeactivatedAt",
                table: "Memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SetupCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IssuedTo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UsedByOrganizationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetupCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SetupCodes_Organizations_UsedByOrganizationId",
                        column: x => x.UsedByOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SetupCodes_Code",
                table: "SetupCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SetupCodes_UsedByOrganizationId",
                table: "SetupCodes",
                column: "UsedByOrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SetupCodes");

            migrationBuilder.DropColumn(
                name: "HoldsStock",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ItemWord",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "TaxPosition",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "AppUsers");
        }
    }
}
