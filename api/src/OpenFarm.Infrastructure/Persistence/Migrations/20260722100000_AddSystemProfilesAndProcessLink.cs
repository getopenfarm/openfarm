using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenFarm.Infrastructure.Persistence;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    // Hand-authored (see AddPartsCatalog's header comment — no dotnet SDK access in this sandbox);
    // one cohesive migration for the whole 009 delta (data-model.md): adds Origin/VendorName/
    // ExternalId/IsCurrentlyOffered/IsSelectable (+ kind-specific fields) to the three existing
    // profile tables so System- and User-sourced rows share one table per kind; adds
    // Printers.ProcessProfileId parallel to the existing MachineProfileId; removes the interim
    // org-level default-machine/default-process/default-filament-per-material surfaces entirely
    // (FR-009/FR-017) — the new columns and the removed columns land together because C# won't
    // compile with both shapes coexisting (tasks.md's Foundational phase).
    [DbContext(typeof(OpenFarmDbContext))]
    [Migration("20260722100000_AddSystemProfilesAndProcessLink")]
    public partial class AddSystemProfilesAndProcessLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- Remove the interim org-level default-machine/default-process pairing/filament-per-material surfaces ---

            migrationBuilder.DropForeignKey(
                name: "FK_Organizations_MachineProfiles_DefaultMachineProfileId",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_DefaultMachineProfileId",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "DefaultMachineProfileId",
                table: "Organizations");

            migrationBuilder.DropForeignKey(
                name: "FK_MachineProfiles_ProcessProfiles_DefaultProcessProfileId",
                table: "MachineProfiles");

            migrationBuilder.DropIndex(
                name: "IX_MachineProfiles_DefaultProcessProfileId",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "DefaultProcessProfileId",
                table: "MachineProfiles");

            migrationBuilder.DropTable(
                name: "OrganizationFilamentDefaults");

            // --- MachineProfiles: System/User origin columns ---

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "MachineProfiles",
                type: "text",
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AddColumn<string>(
                name: "VendorName",
                table: "MachineProfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "MachineProfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrentlyOffered",
                table: "MachineProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSelectable",
                table: "MachineProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "VendorMachineModelName",
                table: "MachineProfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncludedTemplateNames",
                table: "MachineProfiles",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MachineProfiles_OrganizationId_VendorName_ExternalId",
                table: "MachineProfiles",
                columns: new[] { "OrganizationId", "VendorName", "ExternalId" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MachineProfiles_OrganizationId_VendorName_Name",
                table: "MachineProfiles",
                columns: new[] { "OrganizationId", "VendorName", "Name" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NULL");

            // --- ProcessProfiles: same base columns + CompatibleMachineNames ---

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "ProcessProfiles",
                type: "text",
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AddColumn<string>(
                name: "VendorName",
                table: "ProcessProfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "ProcessProfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrentlyOffered",
                table: "ProcessProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSelectable",
                table: "ProcessProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "CompatibleMachineNames",
                table: "ProcessProfiles",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessProfiles_OrganizationId_VendorName_ExternalId",
                table: "ProcessProfiles",
                columns: new[] { "OrganizationId", "VendorName", "ExternalId" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessProfiles_OrganizationId_VendorName_Name",
                table: "ProcessProfiles",
                columns: new[] { "OrganizationId", "VendorName", "Name" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NULL");

            // --- FilamentProfiles: same base columns + CompatibleMachineNames/MaterialType/IncludedTemplateNames ---

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "FilamentProfiles",
                type: "text",
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AddColumn<string>(
                name: "VendorName",
                table: "FilamentProfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "FilamentProfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrentlyOffered",
                table: "FilamentProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSelectable",
                table: "FilamentProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "CompatibleMachineNames",
                table: "FilamentProfiles",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaterialType",
                table: "FilamentProfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncludedTemplateNames",
                table: "FilamentProfiles",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FilamentProfiles_OrganizationId_VendorName_ExternalId",
                table: "FilamentProfiles",
                columns: new[] { "OrganizationId", "VendorName", "ExternalId" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FilamentProfiles_OrganizationId_VendorName_Name",
                table: "FilamentProfiles",
                columns: new[] { "OrganizationId", "VendorName", "Name" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NULL");

            // --- Printers.ProcessProfileId, parallel to the existing MachineProfileId ---

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessProfileId",
                table: "Printers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Printers_ProcessProfileId",
                table: "Printers",
                column: "ProcessProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_Printers_ProcessProfiles_ProcessProfileId",
                table: "Printers",
                column: "ProcessProfileId",
                principalTable: "ProcessProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Printers_ProcessProfiles_ProcessProfileId",
                table: "Printers");

            migrationBuilder.DropIndex(
                name: "IX_Printers_ProcessProfileId",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "ProcessProfileId",
                table: "Printers");

            migrationBuilder.DropIndex(
                name: "IX_FilamentProfiles_OrganizationId_VendorName_ExternalId",
                table: "FilamentProfiles");

            migrationBuilder.DropIndex(
                name: "IX_FilamentProfiles_OrganizationId_VendorName_Name",
                table: "FilamentProfiles");

            migrationBuilder.DropColumn(name: "IncludedTemplateNames", table: "FilamentProfiles");
            migrationBuilder.DropColumn(name: "MaterialType", table: "FilamentProfiles");
            migrationBuilder.DropColumn(name: "CompatibleMachineNames", table: "FilamentProfiles");
            migrationBuilder.DropColumn(name: "IsSelectable", table: "FilamentProfiles");
            migrationBuilder.DropColumn(name: "IsCurrentlyOffered", table: "FilamentProfiles");
            migrationBuilder.DropColumn(name: "ExternalId", table: "FilamentProfiles");
            migrationBuilder.DropColumn(name: "VendorName", table: "FilamentProfiles");
            migrationBuilder.DropColumn(name: "Origin", table: "FilamentProfiles");

            migrationBuilder.DropIndex(
                name: "IX_ProcessProfiles_OrganizationId_VendorName_ExternalId",
                table: "ProcessProfiles");

            migrationBuilder.DropIndex(
                name: "IX_ProcessProfiles_OrganizationId_VendorName_Name",
                table: "ProcessProfiles");

            migrationBuilder.DropColumn(name: "CompatibleMachineNames", table: "ProcessProfiles");
            migrationBuilder.DropColumn(name: "IsSelectable", table: "ProcessProfiles");
            migrationBuilder.DropColumn(name: "IsCurrentlyOffered", table: "ProcessProfiles");
            migrationBuilder.DropColumn(name: "ExternalId", table: "ProcessProfiles");
            migrationBuilder.DropColumn(name: "VendorName", table: "ProcessProfiles");
            migrationBuilder.DropColumn(name: "Origin", table: "ProcessProfiles");

            migrationBuilder.DropIndex(
                name: "IX_MachineProfiles_OrganizationId_VendorName_ExternalId",
                table: "MachineProfiles");

            migrationBuilder.DropIndex(
                name: "IX_MachineProfiles_OrganizationId_VendorName_Name",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(name: "IncludedTemplateNames", table: "MachineProfiles");
            migrationBuilder.DropColumn(name: "VendorMachineModelName", table: "MachineProfiles");
            migrationBuilder.DropColumn(name: "IsSelectable", table: "MachineProfiles");
            migrationBuilder.DropColumn(name: "IsCurrentlyOffered", table: "MachineProfiles");
            migrationBuilder.DropColumn(name: "ExternalId", table: "MachineProfiles");
            migrationBuilder.DropColumn(name: "VendorName", table: "MachineProfiles");
            migrationBuilder.DropColumn(name: "Origin", table: "MachineProfiles");

            // --- Restore the interim org-level default surfaces (forward-only feature: no data backfill) ---

            migrationBuilder.CreateTable(
                name: "OrganizationFilamentDefaults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialType = table.Column<string>(type: "text", nullable: false),
                    FilamentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationFilamentDefaults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationFilamentDefaults_FilamentProfiles_FilamentProfileId",
                        column: x => x.FilamentProfileId,
                        principalTable: "FilamentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationFilamentDefaults_OrganizationId_MaterialType",
                table: "OrganizationFilamentDefaults",
                columns: new[] { "OrganizationId", "MaterialType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationFilamentDefaults_FilamentProfileId",
                table: "OrganizationFilamentDefaults",
                column: "FilamentProfileId");

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultProcessProfileId",
                table: "MachineProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MachineProfiles_DefaultProcessProfileId",
                table: "MachineProfiles",
                column: "DefaultProcessProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_MachineProfiles_ProcessProfiles_DefaultProcessProfileId",
                table: "MachineProfiles",
                column: "DefaultProcessProfileId",
                principalTable: "ProcessProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultMachineProfileId",
                table: "Organizations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_DefaultMachineProfileId",
                table: "Organizations",
                column: "DefaultMachineProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_Organizations_MachineProfiles_DefaultMachineProfileId",
                table: "Organizations",
                column: "DefaultMachineProfileId",
                principalTable: "MachineProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
