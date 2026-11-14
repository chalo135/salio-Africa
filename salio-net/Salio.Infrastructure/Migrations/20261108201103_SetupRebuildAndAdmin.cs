using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SetupRebuildAndAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited: EF scaffolded UsedAt -> RevokedAt and dropped
            // HoldsStock before its values could be read. Every rename below
            // is a RenameColumn, so existing data is kept.

            migrationBuilder.DropForeignKey(
                name: "FK_SetupCodes_Organizations_UsedByOrganizationId",
                table: "SetupCodes");

            // --- SetupCodes: renames, data kept ---
            migrationBuilder.RenameColumn(
                name: "UsedByOrganizationId",
                table: "SetupCodes",
                newName: "OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_SetupCodes_UsedByOrganizationId",
                table: "SetupCodes",
                newName: "IX_SetupCodes_OrganizationId");

            migrationBuilder.RenameColumn(
                name: "UsedAt",
                table: "SetupCodes",
                newName: "RedeemedAt");

            migrationBuilder.RenameColumn(
                name: "IssuedTo",
                table: "SetupCodes",
                newName: "ShopName");

            // --- SetupCodes: new columns ---
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "SetupCodes",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAt",
                table: "SetupCodes",
                type: "timestamp with time zone",
                nullable: true);

            // Codes are now stored upper case. Old-format codes (not six
            // characters) can never be typed into the new form, so any not
            // yet used are revoked. They are kept, not deleted, which is why
            // the Code column stays at 32 characters.
            migrationBuilder.Sql("UPDATE \"SetupCodes\" SET \"Code\" = upper(\"Code\");");
            migrationBuilder.Sql(
                "UPDATE \"SetupCodes\" SET \"RevokedAt\" = now() "
                + "WHERE length(\"Code\") <> 6 AND \"RedeemedAt\" IS NULL AND \"RevokedAt\" IS NULL;");

            // --- Organizations ---
            migrationBuilder.RenameColumn(
                name: "ItemWord",
                table: "Organizations",
                newName: "SellableNoun");

            migrationBuilder.AddColumn<string>(
                name: "Trade",
                table: "Organizations",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BusinessKind",
                table: "Organizations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Sales");

            // HoldsStock true -> "Sales", false -> "Services", then the old
            // column goes.
            migrationBuilder.Sql(
                "UPDATE \"Organizations\" SET \"BusinessKind\" = "
                + "CASE WHEN \"HoldsStock\" THEN 'Sales' ELSE 'Services' END;");

            migrationBuilder.DropColumn(
                name: "HoldsStock",
                table: "Organizations");

            // --- AppUsers ---
            migrationBuilder.AddColumn<bool>(
                name: "IsPlatformAdmin",
                table: "AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // --- AdminAuditLogs ---
            migrationBuilder.CreateTable(
                name: "AdminAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TargetOrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetSetupCodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminAuditLogs_AppUsers_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdminAuditLogs_Organizations_TargetOrganizationId",
                        column: x => x.TargetOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdminAuditLogs_SetupCodes_TargetSetupCodeId",
                        column: x => x.TargetSetupCodeId,
                        principalTable: "SetupCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_AdminUserId",
                table: "AdminAuditLogs",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_TargetOrganizationId",
                table: "AdminAuditLogs",
                column: "TargetOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_TargetSetupCodeId",
                table: "AdminAuditLogs",
                column: "TargetSetupCodeId");

            migrationBuilder.AddForeignKey(
                name: "FK_SetupCodes_Organizations_OrganizationId",
                table: "SetupCodes",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SetupCodes_Organizations_OrganizationId",
                table: "SetupCodes");

            migrationBuilder.DropTable(
                name: "AdminAuditLogs");

            migrationBuilder.DropColumn(
                name: "IsPlatformAdmin",
                table: "AppUsers");

            migrationBuilder.AddColumn<bool>(
                name: "HoldsStock",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql(
                "UPDATE \"Organizations\" SET \"HoldsStock\" = (\"BusinessKind\" <> 'Services');");

            migrationBuilder.DropColumn(
                name: "BusinessKind",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Trade",
                table: "Organizations");

            migrationBuilder.RenameColumn(
                name: "SellableNoun",
                table: "Organizations",
                newName: "ItemWord");

            // The old code compared lower case. Revocations cannot be kept.
            migrationBuilder.Sql("UPDATE \"SetupCodes\" SET \"Code\" = lower(\"Code\");");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "SetupCodes");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "SetupCodes");

            migrationBuilder.RenameColumn(
                name: "ShopName",
                table: "SetupCodes",
                newName: "IssuedTo");

            migrationBuilder.RenameColumn(
                name: "RedeemedAt",
                table: "SetupCodes",
                newName: "UsedAt");

            migrationBuilder.RenameIndex(
                name: "IX_SetupCodes_OrganizationId",
                table: "SetupCodes",
                newName: "IX_SetupCodes_UsedByOrganizationId");

            migrationBuilder.RenameColumn(
                name: "OrganizationId",
                table: "SetupCodes",
                newName: "UsedByOrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_SetupCodes_Organizations_UsedByOrganizationId",
                table: "SetupCodes",
                column: "UsedByOrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
