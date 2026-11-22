using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangesSeenUntil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ChangesSeenUntil",
                table: "Memberships",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangesSeenUntil",
                table: "Memberships");
        }
    }
}
