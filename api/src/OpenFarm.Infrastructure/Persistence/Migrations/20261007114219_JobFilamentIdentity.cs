using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JobFilamentIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasFilamentRequirementOverride",
                table: "PrintJobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Preserve explicit overrides made before the override flag existed. Historical
            // variant choices cannot be reconstructed safely from the current part.
            migrationBuilder.Sql("""
                UPDATE "PrintJobs" j SET "HasFilamentRequirementOverride" = TRUE
                WHERE EXISTS (
                    SELECT 1 FROM "ActivityLogEntries" a
                    WHERE a."EntityType" = 'PrintJob'
                      AND a."EventType" = 'PrintJobFilamentRequirementsChanged'
                      AND a."EntityId" = j."Id"::text
                      AND a."TenantId" = j."TenantId"
                );
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "NarrowFilamentProfileId",
                table: "JobFilamentConsumptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NarrowFilamentVariantId",
                table: "JobFilamentConsumptions",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasFilamentRequirementOverride",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "NarrowFilamentProfileId",
                table: "JobFilamentConsumptions");

            migrationBuilder.DropColumn(
                name: "NarrowFilamentVariantId",
                table: "JobFilamentConsumptions");
        }
    }
}
