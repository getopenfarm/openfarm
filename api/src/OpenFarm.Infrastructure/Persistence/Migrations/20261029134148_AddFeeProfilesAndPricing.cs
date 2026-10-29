using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeeProfilesAndPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FailureAllowancePercent",
                table: "FarmSettings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PricingMode",
                table: "Assemblies",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingValue",
                table: "Assemblies",
                type: "numeric",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FeeProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Fees = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeeProfiles", x => x.Id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Assemblies_Pricing",
                table: "Assemblies",
                sql: "(\"PricingMode\" IS NULL) = (\"PricingValue\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_FeeProfiles_Name",
                table: "FeeProfiles",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeeProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Assemblies_Pricing",
                table: "Assemblies");

            migrationBuilder.DropColumn(
                name: "FailureAllowancePercent",
                table: "FarmSettings");

            migrationBuilder.DropColumn(
                name: "PricingMode",
                table: "Assemblies");

            migrationBuilder.DropColumn(
                name: "PricingValue",
                table: "Assemblies");
        }
    }
}
