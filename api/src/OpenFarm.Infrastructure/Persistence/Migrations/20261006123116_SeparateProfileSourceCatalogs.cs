using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeparateProfileSourceCatalogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessProfiles_OrganizationId_VendorName_ExternalId",
                table: "ProcessProfiles");

            migrationBuilder.DropIndex(
                name: "IX_ProcessProfiles_OrganizationId_VendorName_Name",
                table: "ProcessProfiles");

            migrationBuilder.DropIndex(
                name: "IX_MachineProfiles_OrganizationId_VendorName_ExternalId",
                table: "MachineProfiles");

            migrationBuilder.DropIndex(
                name: "IX_MachineProfiles_OrganizationId_VendorName_Name",
                table: "MachineProfiles");

            migrationBuilder.DropIndex(
                name: "IX_FilamentProfiles_OrganizationId_VendorName_ExternalId",
                table: "FilamentProfiles");

            migrationBuilder.DropIndex(
                name: "IX_FilamentProfiles_OrganizationId_VendorName_Name",
                table: "FilamentProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessProfiles_OrganizationId_SourceFormat_VendorName_Exte~",
                table: "ProcessProfiles",
                columns: new[] { "OrganizationId", "SourceFormat", "VendorName", "ExternalId" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessProfiles_OrganizationId_SourceFormat_VendorName_Name",
                table: "ProcessProfiles",
                columns: new[] { "OrganizationId", "SourceFormat", "VendorName", "Name" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MachineProfiles_OrganizationId_SourceFormat_VendorName_Exte~",
                table: "MachineProfiles",
                columns: new[] { "OrganizationId", "SourceFormat", "VendorName", "ExternalId" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MachineProfiles_OrganizationId_SourceFormat_VendorName_Name",
                table: "MachineProfiles",
                columns: new[] { "OrganizationId", "SourceFormat", "VendorName", "Name" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FilamentProfiles_OrganizationId_SourceFormat_VendorName_Ext~",
                table: "FilamentProfiles",
                columns: new[] { "OrganizationId", "SourceFormat", "VendorName", "ExternalId" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FilamentProfiles_OrganizationId_SourceFormat_VendorName_Name",
                table: "FilamentProfiles",
                columns: new[] { "OrganizationId", "SourceFormat", "VendorName", "Name" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessProfiles_OrganizationId_SourceFormat_VendorName_Exte~",
                table: "ProcessProfiles");

            migrationBuilder.DropIndex(
                name: "IX_ProcessProfiles_OrganizationId_SourceFormat_VendorName_Name",
                table: "ProcessProfiles");

            migrationBuilder.DropIndex(
                name: "IX_MachineProfiles_OrganizationId_SourceFormat_VendorName_Exte~",
                table: "MachineProfiles");

            migrationBuilder.DropIndex(
                name: "IX_MachineProfiles_OrganizationId_SourceFormat_VendorName_Name",
                table: "MachineProfiles");

            migrationBuilder.DropIndex(
                name: "IX_FilamentProfiles_OrganizationId_SourceFormat_VendorName_Ext~",
                table: "FilamentProfiles");

            migrationBuilder.DropIndex(
                name: "IX_FilamentProfiles_OrganizationId_SourceFormat_VendorName_Name",
                table: "FilamentProfiles");

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
        }
    }
}
