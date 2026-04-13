using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCentral.Api.Migrations
{
    /// <inheritdoc />
    public partial class RebuildOrchestrationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQL Server cannot safely alter these existing columns from numeric types
            // to uniqueidentifier in place, so we drop and recreate them.
            // This is acceptable here because the orchestration schema is new.

            migrationBuilder.DropIndex(
                name: "IX_OrchestrationRunSteps_CommandId",
                table: "OrchestrationRunSteps");

            migrationBuilder.DropIndex(
                name: "IX_OrchestrationRuns_DeviceId",
                table: "OrchestrationRuns");

            migrationBuilder.DropIndex(
                name: "IX_EnrollmentActions_DeviceId",
                table: "EnrollmentActions");

            migrationBuilder.DropColumn(
                name: "CommandId",
                table: "OrchestrationRunSteps");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "OrchestrationRuns");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "EnrollmentActions");

            migrationBuilder.AddColumn<Guid>(
                name: "CommandId",
                table: "OrchestrationRunSteps",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeviceId",
                table: "OrchestrationRuns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeviceId",
                table: "EnrollmentActions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationRunSteps_CommandId",
                table: "OrchestrationRunSteps",
                column: "CommandId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationRuns_DeviceId",
                table: "OrchestrationRuns",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentActions_DeviceId",
                table: "EnrollmentActions",
                column: "DeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrchestrationRunSteps_CommandId",
                table: "OrchestrationRunSteps");

            migrationBuilder.DropIndex(
                name: "IX_OrchestrationRuns_DeviceId",
                table: "OrchestrationRuns");

            migrationBuilder.DropIndex(
                name: "IX_EnrollmentActions_DeviceId",
                table: "EnrollmentActions");

            migrationBuilder.DropColumn(
                name: "CommandId",
                table: "OrchestrationRunSteps");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "OrchestrationRuns");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "EnrollmentActions");

            migrationBuilder.AddColumn<long>(
                name: "CommandId",
                table: "OrchestrationRunSteps",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeviceId",
                table: "OrchestrationRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeviceId",
                table: "EnrollmentActions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationRunSteps_CommandId",
                table: "OrchestrationRunSteps",
                column: "CommandId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationRuns_DeviceId",
                table: "OrchestrationRuns",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentActions_DeviceId",
                table: "EnrollmentActions",
                column: "DeviceId");
        }
    }
}