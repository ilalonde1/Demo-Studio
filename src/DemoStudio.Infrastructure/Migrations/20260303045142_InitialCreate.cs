using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DemoStudio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DemoProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemoProjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationTargets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DemoProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApplicationType = table.Column<int>(type: "int", nullable: false),
                    TargetReference = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationTargets_DemoProjects_DemoProjectId",
                        column: x => x.DemoProjectId,
                        principalTable: "DemoProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DemoFlows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DemoProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsDeterministic = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemoFlows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DemoFlows_DemoProjects_DemoProjectId",
                        column: x => x.DemoProjectId,
                        principalTable: "DemoProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RedactionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DemoProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MatchExpression = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ReplacementText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedactionRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RedactionRules_DemoProjects_DemoProjectId",
                        column: x => x.DemoProjectId,
                        principalTable: "DemoProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DemoRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DemoProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DemoFlowId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    QueuedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OutputDirectory = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    RawVideoPath = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    RedactedVideoPath = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    LogPath = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    OutputVideoPath = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemoRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DemoRuns_DemoFlows_DemoFlowId",
                        column: x => x.DemoFlowId,
                        principalTable: "DemoFlows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DemoRuns_DemoProjects_DemoProjectId",
                        column: x => x.DemoProjectId,
                        principalTable: "DemoProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FlowSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DemoFlowId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    StepType = table.Column<int>(type: "int", nullable: false),
                    ActionKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlowSteps_DemoFlows_DemoFlowId",
                        column: x => x.DemoFlowId,
                        principalTable: "DemoFlows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationTargets_DemoProjectId_Name",
                table: "ApplicationTargets",
                columns: new[] { "DemoProjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DemoFlows_DemoProjectId_Name_Version",
                table: "DemoFlows",
                columns: new[] { "DemoProjectId", "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DemoProjects_Code",
                table: "DemoProjects",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DemoRuns_DemoFlowId",
                table: "DemoRuns",
                column: "DemoFlowId");

            migrationBuilder.CreateIndex(
                name: "IX_DemoRuns_DemoProjectId",
                table: "DemoRuns",
                column: "DemoProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DemoRuns_QueuedAtUtc",
                table: "DemoRuns",
                column: "QueuedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DemoRuns_Status",
                table: "DemoRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FlowSteps_DemoFlowId_Sequence",
                table: "FlowSteps",
                columns: new[] { "DemoFlowId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RedactionRules_DemoProjectId_Name",
                table: "RedactionRules",
                columns: new[] { "DemoProjectId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationTargets");

            migrationBuilder.DropTable(
                name: "DemoRuns");

            migrationBuilder.DropTable(
                name: "FlowSteps");

            migrationBuilder.DropTable(
                name: "RedactionRules");

            migrationBuilder.DropTable(
                name: "DemoFlows");

            migrationBuilder.DropTable(
                name: "DemoProjects");
        }
    }
}
