using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCentral.Api.Migrations
{
    /// <inheritdoc />
    public partial class UserActivityPhase1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserActivityHistories",
                columns: table => new
                {
                    UserActivityHistoryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CapturedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastInputUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IdleSeconds = table.Column<int>(type: "int", nullable: true),
                    SessionState = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsUserActive = table.Column<bool>(type: "bit", nullable: true),
                    IsPosForeground = table.Column<bool>(type: "bit", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserActivityHistories", x => x.UserActivityHistoryId);
                    table.ForeignKey(
                        name: "FK_UserActivityHistories_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "DeviceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserActivityInventories",
                columns: table => new
                {
                    UserActivityInventoryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CapturedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastInputUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IdleSeconds = table.Column<int>(type: "int", nullable: true),
                    SessionState = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ConsoleUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsUserActive = table.Column<bool>(type: "bit", nullable: true),
                    IsPosForeground = table.Column<bool>(type: "bit", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserActivityInventories", x => x.UserActivityInventoryId);
                    table.ForeignKey(
                        name: "FK_UserActivityInventories_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "DeviceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserActivityHistories_DeviceId_CapturedUtc",
                table: "UserActivityHistories",
                columns: new[] { "DeviceId", "CapturedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserActivityInventories_DeviceId",
                table: "UserActivityInventories",
                column: "DeviceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserActivityHistories");

            migrationBuilder.DropTable(
                name: "UserActivityInventories");
        }
    }
}
