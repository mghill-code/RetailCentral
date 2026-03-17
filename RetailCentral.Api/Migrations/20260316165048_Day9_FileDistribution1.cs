using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCentral.Api.Migrations
{
    /// <inheritdoc />
    public partial class Day9_FileDistribution1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Packages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PackageType = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ExecutionCommand = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExecutionArguments = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    WorkingDirectory = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    RebootBehavior = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Packages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Deployments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackageId = table.Column<int>(type: "int", nullable: false),
                    TargetType = table.Column<int>(type: "int", nullable: false),
                    TargetValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExecuteMode = table.Column<int>(type: "int", nullable: false),
                    WindowStartLocal = table.Column<TimeSpan>(type: "time", nullable: true),
                    WindowEndLocal = table.Column<TimeSpan>(type: "time", nullable: true),
                    UseStoreLocalTime = table.Column<bool>(type: "bit", nullable: false),
                    AllowOutsideWindow = table.Column<bool>(type: "bit", nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deployments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deployments_Packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentDevices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeploymentId = table.Column<int>(type: "int", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Hostname = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DownloadStatus = table.Column<int>(type: "int", nullable: false),
                    ExecuteStatus = table.Column<int>(type: "int", nullable: false),
                    DownloadStartedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DownloadCompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExecuteStartedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExecuteCompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExitCode = table.Column<int>(type: "int", nullable: true),
                    ResultMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastHeartbeatUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentDevices_Deployments_DeploymentId",
                        column: x => x.DeploymentId,
                        principalTable: "Deployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentDevices_DeploymentId",
                table: "DeploymentDevices",
                column: "DeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentDevices_DeviceId",
                table: "DeploymentDevices",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentDevices_Status",
                table: "DeploymentDevices",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_PackageId",
                table: "Deployments",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_Status",
                table: "Deployments",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeploymentDevices");

            migrationBuilder.DropTable(
                name: "Deployments");

            migrationBuilder.DropTable(
                name: "Packages");
        }
    }
}
