using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCentral.Api.Migrations
{
    /// <inheritdoc />
    public partial class HeartbeatDeviceTimestampIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Heartbeats_DeviceId",
                table: "Heartbeats");

            migrationBuilder.CreateIndex(
                name: "IX_Heartbeats_DeviceId_TimestampUtc",
                table: "Heartbeats",
                columns: new[] { "DeviceId", "TimestampUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Heartbeats_DeviceId_TimestampUtc",
                table: "Heartbeats");

            migrationBuilder.CreateIndex(
                name: "IX_Heartbeats_DeviceId",
                table: "Heartbeats",
                column: "DeviceId");
        }
    }
}
