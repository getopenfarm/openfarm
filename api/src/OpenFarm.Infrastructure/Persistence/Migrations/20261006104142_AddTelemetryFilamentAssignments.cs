using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetryFilamentAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilamentProfileId",
                table: "Filaments");

            migrationBuilder.AddColumn<bool>(
                name: "NozzleConfirmed",
                table: "Printers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NozzleObservedAtUtc",
                table: "Printers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReportedNozzleDiameterMm",
                table: "Printers",
                type: "numeric",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FilamentProfileAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Brand = table.Column<string>(type: "text", nullable: false),
                    ModelCode = table.Column<string>(type: "text", nullable: false),
                    NozzleDiameterMm = table.Column<decimal>(type: "numeric", nullable: false),
                    FilamentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilamentVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilamentProfileAssignments", x => x.Id);
                    table.CheckConstraint("CK_FilamentAssignment_Nozzle", "\"NozzleDiameterMm\" > 0");
                    table.ForeignKey(
                        name: "FK_FilamentProfileAssignments_FilamentProfiles_FilamentProfile~",
                        column: x => x.FilamentProfileId,
                        principalTable: "FilamentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FilamentProfileAssignments_FilamentVariants_FilamentVariant~",
                        column: x => x.FilamentVariantId,
                        principalTable: "FilamentVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FilamentProfileAssignments_Filaments_FilamentId",
                        column: x => x.FilamentId,
                        principalTable: "Filaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FilamentProfileAssignments_FilamentId",
                table: "FilamentProfileAssignments",
                column: "FilamentId");

            migrationBuilder.CreateIndex(
                name: "IX_FilamentProfileAssignments_FilamentProfileId",
                table: "FilamentProfileAssignments",
                column: "FilamentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_FilamentProfileAssignments_FilamentVariantId",
                table: "FilamentProfileAssignments",
                column: "FilamentVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_FilamentProfileAssignments_OrganizationId_FilamentId_Brand_~",
                table: "FilamentProfileAssignments",
                columns: new[] { "OrganizationId", "FilamentId", "Brand", "ModelCode", "NozzleDiameterMm" },
                unique: true,
                filter: "\"FilamentVariantId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FilamentProfileAssignments_OrganizationId_FilamentId_Brand~1",
                table: "FilamentProfileAssignments",
                columns: new[] { "OrganizationId", "FilamentId", "Brand", "ModelCode", "NozzleDiameterMm", "FilamentVariantId" },
                unique: true,
                filter: "\"FilamentVariantId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FilamentProfileAssignments");

            migrationBuilder.DropColumn(
                name: "NozzleConfirmed",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "NozzleObservedAtUtc",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "ReportedNozzleDiameterMm",
                table: "Printers");

            migrationBuilder.AddColumn<Guid>(
                name: "FilamentProfileId",
                table: "Filaments",
                type: "uuid",
                nullable: true);
        }
    }
}
