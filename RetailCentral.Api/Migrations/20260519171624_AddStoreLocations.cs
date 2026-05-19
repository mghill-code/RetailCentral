using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCentral.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoreLocations",
                columns: table => new
                {
                    StoreLocationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StoreName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StoreAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StoreCity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StoreState = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    StoreZipCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    CoordinatesSource = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreLocations", x => x.StoreLocationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreLocations_StoreNumber",
                table: "StoreLocations",
                column: "StoreNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreLocations");
        }
    }
}
