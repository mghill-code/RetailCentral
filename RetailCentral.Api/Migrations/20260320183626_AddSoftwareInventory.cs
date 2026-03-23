using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCentral.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftwareInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InstalledSoftwares",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Publisher = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    InstallDate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstalledSoftwares", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InstalledWindowsUpdates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HotFixId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    InstalledOn = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstalledWindowsUpdates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InstalledSoftwares_DeviceId",
                table: "InstalledSoftwares",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_InstalledWindowsUpdates_DeviceId",
                table: "InstalledWindowsUpdates",
                column: "DeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstalledSoftwares");

            migrationBuilder.DropTable(
                name: "InstalledWindowsUpdates");
        }
    }
}
