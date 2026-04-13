using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCentral.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOrchestrationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrchestrationTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    DeviceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Environment = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TriggerType = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrchestrationTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrchestrationRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: true),
                    RegisterId = table.Column<int>(type: "int", nullable: true),
                    AgentId = table.Column<int>(type: "int", nullable: true),
                    DeviceId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CurrentStepOrder = table.Column<int>(type: "int", nullable: true),
                    TriggerSource = table.Column<int>(type: "int", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrchestrationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrchestrationRuns_OrchestrationTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "OrchestrationTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrchestrationTemplateSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StepType = table.Column<int>(type: "int", nullable: false),
                    CommandType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SuccessCriteriaJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    MaxRetries = table.Column<int>(type: "int", nullable: false),
                    OnFailureAction = table.Column<int>(type: "int", nullable: false),
                    ContinueOnFailure = table.Column<bool>(type: "bit", nullable: false),
                    RollbackTemplateStepId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrchestrationTemplateSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrchestrationTemplateSteps_OrchestrationTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "OrchestrationTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProvisioningProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DeviceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StoreGroup = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Environment = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvisioningProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProvisioningProfiles_OrchestrationTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "OrchestrationTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrchestrationRunSteps",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    TemplateStepId = table.Column<int>(type: "int", nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    LogsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CommandId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrchestrationRunSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrchestrationRunSteps_OrchestrationRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "OrchestrationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrchestrationRunSteps_OrchestrationTemplateSteps_TemplateStepId",
                        column: x => x.TemplateStepId,
                        principalTable: "OrchestrationTemplateSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EnrollmentActions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: true),
                    AgentId = table.Column<int>(type: "int", nullable: true),
                    AssignedProfileId = table.Column<int>(type: "int", nullable: true),
                    InitialRunId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrollmentActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnrollmentActions_OrchestrationRuns_InitialRunId",
                        column: x => x.InitialRunId,
                        principalTable: "OrchestrationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnrollmentActions_ProvisioningProfiles_AssignedProfileId",
                        column: x => x.AssignedProfileId,
                        principalTable: "ProvisioningProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentActions_AgentId",
                table: "EnrollmentActions",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentActions_AssignedProfileId",
                table: "EnrollmentActions",
                column: "AssignedProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentActions_DeviceId",
                table: "EnrollmentActions",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentActions_InitialRunId",
                table: "EnrollmentActions",
                column: "InitialRunId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentActions_Status",
                table: "EnrollmentActions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationRuns_AgentId",
                table: "OrchestrationRuns",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationRuns_CorrelationId",
                table: "OrchestrationRuns",
                column: "CorrelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationRuns_DeviceId",
                table: "OrchestrationRuns",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationRuns_RegisterId",
                table: "OrchestrationRuns",
                column: "RegisterId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationRuns_Status",
                table: "OrchestrationRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationRuns_StoreId",
                table: "OrchestrationRuns",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationRuns_TemplateId",
                table: "OrchestrationRuns",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationRunSteps_CommandId",
                table: "OrchestrationRunSteps",
                column: "CommandId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationRunSteps_RunId",
                table: "OrchestrationRunSteps",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationRunSteps_RunId_StepOrder",
                table: "OrchestrationRunSteps",
                columns: new[] { "RunId", "StepOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationRunSteps_TemplateStepId",
                table: "OrchestrationRunSteps",
                column: "TemplateStepId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationTemplates_IsActive",
                table: "OrchestrationTemplates",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationTemplates_Name_Version",
                table: "OrchestrationTemplates",
                columns: new[] { "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationTemplateSteps_StepType",
                table: "OrchestrationTemplateSteps",
                column: "StepType");

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationTemplateSteps_TemplateId_StepOrder",
                table: "OrchestrationTemplateSteps",
                columns: new[] { "TemplateId", "StepOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningProfiles_DeviceType",
                table: "ProvisioningProfiles",
                column: "DeviceType");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningProfiles_Environment",
                table: "ProvisioningProfiles",
                column: "Environment");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningProfiles_IsActive",
                table: "ProvisioningProfiles",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningProfiles_IsDefault",
                table: "ProvisioningProfiles",
                column: "IsDefault");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningProfiles_TemplateId",
                table: "ProvisioningProfiles",
                column: "TemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnrollmentActions");

            migrationBuilder.DropTable(
                name: "OrchestrationRunSteps");

            migrationBuilder.DropTable(
                name: "ProvisioningProfiles");

            migrationBuilder.DropTable(
                name: "OrchestrationRuns");

            migrationBuilder.DropTable(
                name: "OrchestrationTemplateSteps");

            migrationBuilder.DropTable(
                name: "OrchestrationTemplates");
        }
    }
}
