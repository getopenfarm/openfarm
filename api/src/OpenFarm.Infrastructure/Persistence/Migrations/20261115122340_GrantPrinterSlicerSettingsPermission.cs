using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GrantPrinterSlicerSettingsPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The build plate moved from "change connection" to its own permission. Operators swap plates, so they
            // get it; every other role that could set the plate before (it held printers:edit-connection) keeps
            // that ability. Administrators hold every permission already.
            migrationBuilder.Sql("""
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue")
                SELECT DISTINCT "RoleId", 'openfarm.permission', 'printers:edit-slicer-settings'
                FROM "AspNetRoleClaims"
                WHERE "ClaimType" = 'openfarm.permission' AND "ClaimValue" = 'printers:edit-connection'
                UNION
                SELECT r."Id", 'openfarm.permission', 'printers:edit-slicer-settings'
                FROM "AspNetRoles" r
                WHERE r."Id" = 'f246d189-b11c-4494-9999-000000000002'::uuid;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DELETE FROM "AspNetRoleClaims" WHERE "ClaimValue" = 'printers:edit-slicer-settings'""");
        }
    }
}
