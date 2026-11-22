using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LastSignedInAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSignedInAt",
                table: "AppUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PreviousSignedInAt",
                table: "AppUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSignedInAt",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "PreviousSignedInAt",
                table: "AppUsers");
        }
    }
}
