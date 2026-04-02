using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCentral.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCommandNextAttemptUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptUtc",
                table: "Commands",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Commands_NextAttemptUtc",
                table: "Commands",
                column: "NextAttemptUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Commands_Status_NextAttemptUtc",
                table: "Commands",
                columns: new[] { "Status", "NextAttemptUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Commands_NextAttemptUtc",
                table: "Commands");

            migrationBuilder.DropIndex(
                name: "IX_Commands_Status_NextAttemptUtc",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "NextAttemptUtc",
                table: "Commands");
        }
    }
}
