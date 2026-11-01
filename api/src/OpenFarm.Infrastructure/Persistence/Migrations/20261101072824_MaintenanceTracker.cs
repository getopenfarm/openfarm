using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MaintenanceTracker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaintenanceTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    EveryPrintHours = table.Column<decimal>(type: "numeric", nullable: true),
                    EveryDays = table.Column<int>(type: "integer", nullable: true),
                    EveryPrints = table.Column<int>(type: "integer", nullable: true),
                    WarnAtPercent = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceTasks", x => x.Id);
                    table.CheckConstraint("CK_MaintenanceTasks_Intervals", "(\"EveryPrintHours\" IS NOT NULL OR \"EveryDays\" IS NOT NULL OR \"EveryPrints\" IS NOT NULL) AND coalesce(\"EveryPrintHours\", 1) > 0 AND coalesce(\"EveryDays\", 1) > 0 AND coalesce(\"EveryPrints\", 1) > 0 AND \"WarnAtPercent\" BETWEEN 1 AND 99");
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleStartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CarryHours = table.Column<decimal>(type: "numeric", nullable: false),
                    CarryPrints = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceEnrollments", x => x.Id);
                    table.CheckConstraint("CK_MaintenanceEnrollments_Carries", "\"CarryHours\" >= 0 AND \"CarryPrints\" >= 0");
                    table.ForeignKey(
                        name: "FK_MaintenanceEnrollments_MaintenanceTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "MaintenanceTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaintenanceEnrollments_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskName = table.Column<string>(type: "text", nullable: false),
                    PrinterName = table.Column<string>(type: "text", nullable: false),
                    PerformedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PerformedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformedByNote = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Cost = table.Column<decimal>(type: "numeric", nullable: true),
                    HoursAtCompletion = table.Column<decimal>(type: "numeric", nullable: false),
                    PrintsAtCompletion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceLogs", x => x.Id);
                    table.CheckConstraint("CK_MaintenanceLogs_Cost", "\"Cost\" IS NULL OR \"Cost\" >= 0");
                    table.ForeignKey(
                        name: "FK_MaintenanceLogs_MaintenanceEnrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "MaintenanceEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MaintenanceLogs_MaintenanceTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "MaintenanceTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MaintenanceLogs_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceEnrollments_PrinterId",
                table: "MaintenanceEnrollments",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceEnrollments_TaskId_PrinterId",
                table: "MaintenanceEnrollments",
                columns: new[] { "TaskId", "PrinterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceLogs_EnrollmentId",
                table: "MaintenanceLogs",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceLogs_PrinterId_PerformedAtUtc",
                table: "MaintenanceLogs",
                columns: new[] { "PrinterId", "PerformedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceLogs_TaskId",
                table: "MaintenanceLogs",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTasks_Name",
                table: "MaintenanceTasks",
                column: "Name",
                unique: true);

            // Operators view, mark done and change; Viewers view. Administrators hold every permission already.
            migrationBuilder.Sql("""
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue")
                SELECT r."Id", 'openfarm.permission', c.permission
                FROM "AspNetRoles" r
                JOIN (VALUES ('f246d189-b11c-4494-9999-000000000002'::uuid, 'maintenance:view'),
                             ('f246d189-b11c-4494-9999-000000000002'::uuid, 'maintenance:complete'),
                             ('f246d189-b11c-4494-9999-000000000002'::uuid, 'maintenance:manage'),
                             ('f246d189-b11c-4494-9999-000000000003'::uuid, 'maintenance:view')) AS c(role_id, permission)
                  ON c.role_id = r."Id";
                """);

            // Default tasks (DefaultMaintenance.Names), after Printago's list. Ordinary rows: editable, deletable, never re-created.
            migrationBuilder.Sql("""
                INSERT INTO "MaintenanceTasks" ("Id", "Name", "Notes", "EveryPrintHours", "EveryDays", "EveryPrints", "WarnAtPercent", "CreatedAtUtc")
                VALUES
                  ('6d1f0c3a-0000-4000-8000-000000000001', 'XY-Axis Cleaning & Lubrication (High Use)', 'Wipe the X/Y rods or rails, then re-grease; for printers running most of the day.', NULL, 30, NULL, 80, now()),
                  ('6d1f0c3a-0000-4000-8000-000000000002', 'Air Filter Replacement', 'Replace the chamber activated-carbon filter.', NULL, 90, NULL, 80, now()),
                  ('6d1f0c3a-0000-4000-8000-000000000003', 'Carbon Rod Cleaning', 'Wipe the X/Y carbon rods with a dry lint-free cloth or IPA; no grease.', NULL, 30, NULL, 80, now()),
                  ('6d1f0c3a-0000-4000-8000-000000000004', 'Z-Axis Lead Screw Lubrication', 'Wipe old grease off the Z lead screws, then apply a thin coat of fresh grease.', NULL, 90, NULL, 80, now()),
                  ('6d1f0c3a-0000-4000-8000-000000000005', 'Camera Lens Cleaning', 'Wipe the camera lens with a dry lint-free cloth.', NULL, 30, NULL, 80, now()),
                  ('6d1f0c3a-0000-4000-8000-000000000006', 'XY-Axis Cleaning & Lubrication', 'Wipe the X/Y rods or rails, then apply a thin coat of grease.', NULL, 30, NULL, 80, now()),
                  ('6d1f0c3a-0000-4000-8000-000000000007', 'Full Calibration Run', 'Run the printer''s full calibration: bed leveling, vibration compensation, flow.', 100, NULL, NULL, 80, now()),
                  ('6d1f0c3a-0000-4000-8000-000000000008', 'Rail / Axis Lubrication', 'Wipe the linear rails, then grease them and move each axis end to end.', 100, NULL, NULL, 80, now()),
                  ('6d1f0c3a-0000-4000-8000-000000000009', 'Extruder Gear Cleaning — Standard', 'Brush filament dust out of the extruder gears.', NULL, NULL, 20, 80, now()),
                  ('6d1f0c3a-0000-4000-8000-00000000000a', 'Extruder Gear Cleaning — CF Filament', 'Brush carbon-fiber dust out of the extruder gears; check them for wear.', NULL, NULL, 8, 80, now())
                ON CONFLICT ("Name") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DELETE FROM "MaintenanceTasks" WHERE "Id" IN ('6d1f0c3a-0000-4000-8000-000000000001', '6d1f0c3a-0000-4000-8000-000000000002', '6d1f0c3a-0000-4000-8000-000000000003', '6d1f0c3a-0000-4000-8000-000000000004', '6d1f0c3a-0000-4000-8000-000000000005', '6d1f0c3a-0000-4000-8000-000000000006', '6d1f0c3a-0000-4000-8000-000000000007', '6d1f0c3a-0000-4000-8000-000000000008', '6d1f0c3a-0000-4000-8000-000000000009', '6d1f0c3a-0000-4000-8000-00000000000a')""");
            migrationBuilder.Sql("DELETE FROM \"AspNetRoleClaims\" WHERE \"ClaimType\" = 'openfarm.permission' AND \"ClaimValue\" LIKE 'maintenance:%'");
            migrationBuilder.DropTable(
                name: "MaintenanceLogs");

            migrationBuilder.DropTable(
                name: "MaintenanceEnrollments");

            migrationBuilder.DropTable(
                name: "MaintenanceTasks");
        }
    }
}
