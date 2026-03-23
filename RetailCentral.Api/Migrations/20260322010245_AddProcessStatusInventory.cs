using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCentral.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessStatusInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessStatusInventories",
                columns: table => new
                {
                    ProcessStatusInventoryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PosProcessName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PosRunning = table.Column<bool>(type: "bit", nullable: false),
                    PosProcessCount = table.Column<int>(type: "int", nullable: false),
                    PosCpuPercent = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PosWorkingSetMb = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PosStartedAtLocal = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetailShellProcessName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RetailShellRunning = table.Column<bool>(type: "bit", nullable: false),
                    RetailShellProcessCount = table.Column<int>(type: "int", nullable: false),
                    RetailShellCpuPercent = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RetailShellWorkingSetMb = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RetailShellStartedAtLocal = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AgentProcessName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AgentRunning = table.Column<bool>(type: "bit", nullable: false),
                    AgentProcessCount = table.Column<int>(type: "int", nullable: false),
                    AgentCpuPercent = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AgentWorkingSetMb = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AgentStartedAtLocal = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessStatusInventories", x => x.ProcessStatusInventoryId);
                    table.ForeignKey(
                        name: "FK_ProcessStatusInventories_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "DeviceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessStatusInventories_DeviceId",
                table: "ProcessStatusInventories",
                column: "DeviceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessStatusInventories");
        }
    }
}
