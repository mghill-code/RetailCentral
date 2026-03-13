using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCentral.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRegisterInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegisterInventories",
                columns: table => new
                {
                    RegisterInventoryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComputerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Store = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RegisterNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IPAddress = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MACAddress = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Manufacturer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Memory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HardDriveSize = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HardDriveFreeSpace = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StoreName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StoreAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StoreCity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StoreState = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    StoreZipCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ReleaseLevel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReleaseApplied = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Domain = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastReboot = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SystemBuildDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OSVersion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CPUArch = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    VerifoneModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    VerifoneIP = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ScannerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ScannerSerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastHeartbeatUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegisterInventories", x => x.RegisterInventoryId);
                    table.ForeignKey(
                        name: "FK_RegisterInventories_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "DeviceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegisterInventories_DeviceId",
                table: "RegisterInventories",
                column: "DeviceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegisterInventories");
        }
    }
}
