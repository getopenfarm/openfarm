using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionRunsAndJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Priority = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RunLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetKind = table.Column<string>(type: "text", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: true),
                    RevisionMode = table.Column<string>(type: "text", nullable: false),
                    DefaultAssignmentMode = table.Column<string>(type: "text", nullable: false),
                    DefaultPinnedPrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    BackfillPrinterCap = table.Column<int>(type: "integer", nullable: false),
                    BackfillCumulativeUnitCeiling = table.Column<int>(type: "integer", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunLines", x => x.Id);
                    table.CheckConstraint("CK_RunLines_PinnedRequiresPrinter", "\"DefaultAssignmentMode\" = 'Unassigned' OR \"DefaultPinnedPrinterId\" IS NOT NULL");
                    table.CheckConstraint("CK_RunLines_QuantityAtLeastOne", "\"Quantity\" IS NULL OR \"Quantity\" >= 1");
                    table.ForeignKey(
                        name: "FK_RunLines_Printers_DefaultPinnedPrinterId",
                        column: x => x.DefaultPinnedPrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RunLines_ProductionRuns_ProductionRunId",
                        column: x => x.ProductionRunId,
                        principalTable: "ProductionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartRequirements_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartRequirements_RunLines_RunLineId",
                        column: x => x.RunLineId,
                        principalTable: "RunLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrintJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    QueuePosition = table.Column<double>(type: "double precision", nullable: false),
                    PlateQuantity = table.Column<int>(type: "integer", nullable: false),
                    AssignmentMode = table.Column<string>(type: "text", nullable: false),
                    PinnedPrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchedPrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    FileRevisionHash = table.Column<string>(type: "text", nullable: false),
                    FileRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    DispatchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    CancelRequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequeuedAsJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintJobs_PartRequirements_PartRequirementId",
                        column: x => x.PartRequirementId,
                        principalTable: "PartRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PrintJobs_PrintJobs_RequeuedAsJobId",
                        column: x => x.RequeuedAsJobId,
                        principalTable: "PrintJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PrintJobs_Printers_DispatchedPrinterId",
                        column: x => x.DispatchedPrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PrintJobs_Printers_PinnedPrinterId",
                        column: x => x.PinnedPrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "JobFilamentConsumptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrintJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialType = table.Column<string>(type: "text", nullable: true),
                    DetectedColorHex = table.Column<string>(type: "text", nullable: true),
                    SpoolId = table.Column<Guid>(type: "uuid", nullable: true),
                    GramsConsumed = table.Column<decimal>(type: "numeric", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobFilamentConsumptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobFilamentConsumptions_PrintJobs_PrintJobId",
                        column: x => x.PrintJobId,
                        principalTable: "PrintJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobFilamentConsumptions_Spools_SpoolId",
                        column: x => x.SpoolId,
                        principalTable: "Spools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobFilamentConsumptions_PrintJobId",
                table: "JobFilamentConsumptions",
                column: "PrintJobId");

            migrationBuilder.CreateIndex(
                name: "IX_JobFilamentConsumptions_SpoolId",
                table: "JobFilamentConsumptions",
                column: "SpoolId");

            migrationBuilder.CreateIndex(
                name: "IX_PartRequirements_PartId",
                table: "PartRequirements",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_PartRequirements_RunLineId_PartId",
                table: "PartRequirements",
                columns: new[] { "RunLineId", "PartId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_DispatchedPrinterId",
                table: "PrintJobs",
                column: "DispatchedPrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_OrganizationId_PinnedPrinterId",
                table: "PrintJobs",
                columns: new[] { "OrganizationId", "PinnedPrinterId" },
                filter: "\"PinnedPrinterId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_OrganizationId_Status_AssignmentMode_QueuePosition",
                table: "PrintJobs",
                columns: new[] { "OrganizationId", "Status", "AssignmentMode", "QueuePosition" });

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_PartRequirementId",
                table: "PrintJobs",
                column: "PartRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_PinnedPrinterId",
                table: "PrintJobs",
                column: "PinnedPrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_RequeuedAsJobId",
                table: "PrintJobs",
                column: "RequeuedAsJobId");

            migrationBuilder.CreateIndex(
                name: "IX_RunLines_DefaultPinnedPrinterId",
                table: "RunLines",
                column: "DefaultPinnedPrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_RunLines_ProductionRunId",
                table: "RunLines",
                column: "ProductionRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobFilamentConsumptions");

            migrationBuilder.DropTable(
                name: "PrintJobs");

            migrationBuilder.DropTable(
                name: "PartRequirements");

            migrationBuilder.DropTable(
                name: "RunLines");

            migrationBuilder.DropTable(
                name: "ProductionRuns");
        }
    }
}
