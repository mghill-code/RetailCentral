using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCentral.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProvisioningProfilePriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "ProvisioningProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningProfiles_Priority",
                table: "ProvisioningProfiles",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningProfiles_StoreGroup",
                table: "ProvisioningProfiles",
                column: "StoreGroup");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProvisioningProfiles_Priority",
                table: "ProvisioningProfiles");

            migrationBuilder.DropIndex(
                name: "IX_ProvisioningProfiles_StoreGroup",
                table: "ProvisioningProfiles");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "ProvisioningProfiles");
        }
    }
}
