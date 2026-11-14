using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatalogueImportAndInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                table: "Products",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Products",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReorderLevel",
                table: "Products",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowsCreated = table.Column<int>(type: "integer", nullable: false),
                    RowsUpdated = table.Column<int>(type: "integer", nullable: false),
                    UndoneAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UndoneByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportBatches_AppUsers_UndoneByUserId",
                        column: x => x.UndoneByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportBatches_AppUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportBatches_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImagePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SupplierName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    InvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExtractedTotalMinor = table.Column<long>(type: "bigint", nullable: true),
                    RawExtractionJson = table.Column<string>(type: "text", nullable: true),
                    CommittedStockReceiptId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierInvoices_AppUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatchChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeType = table.Column<int>(type: "integer", nullable: false),
                    PreviousValuesJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatchChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportBatchChanges_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportBatchChanges_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    RawText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ProposedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProposedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ProposedUnitCostMinor = table.Column<long>(type: "bigint", nullable: false),
                    MatchedProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Accepted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierInvoiceLines_Products_MatchedProductId",
                        column: x => x.MatchedProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierInvoiceLines_SupplierInvoices_SupplierInvoiceId",
                        column: x => x.SupplierInvoiceId,
                        principalTable: "SupplierInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatchChanges_ImportBatchId",
                table: "ImportBatchChanges",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatchChanges_OrganizationId_ImportBatchId",
                table: "ImportBatchChanges",
                columns: new[] { "OrganizationId", "ImportBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatchChanges_ProductId",
                table: "ImportBatchChanges",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_OrganizationId_StartedAt",
                table: "ImportBatches",
                columns: new[] { "OrganizationId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_UndoneByUserId",
                table: "ImportBatches",
                column: "UndoneByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_UploadedByUserId",
                table: "ImportBatches",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoiceLines_MatchedProductId",
                table: "SupplierInvoiceLines",
                column: "MatchedProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoiceLines_SupplierInvoiceId_LineNumber",
                table: "SupplierInvoiceLines",
                columns: new[] { "SupplierInvoiceId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoices_OrganizationId_UploadedAt",
                table: "SupplierInvoices",
                columns: new[] { "OrganizationId", "UploadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoices_UploadedByUserId",
                table: "SupplierInvoices",
                column: "UploadedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportBatchChanges");

            migrationBuilder.DropTable(
                name: "SupplierInvoiceLines");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "SupplierInvoices");

            migrationBuilder.DropColumn(
                name: "Barcode",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ReorderLevel",
                table: "Products");
        }
    }
}
