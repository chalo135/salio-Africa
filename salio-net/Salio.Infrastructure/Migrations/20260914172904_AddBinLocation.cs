using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBinLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BinLocation",
                table: "Products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BinLocation",
                table: "Products");
        }
    }
}
